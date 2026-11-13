using System.Collections.Immutable;
using System.Diagnostics;
using MultiSound.Audio.Backend;

namespace MultiSound.Audio;

/// <param name="Volume">Copy volume, 0–1.</param>
/// <param name="OffsetMs">User offset, 0–500 ms.</param>
public sealed record OutputSettings(float Volume, double OffsetMs);

public sealed record OutputFault(string DeviceId, string Message);

/// <summary>Copies everything the main device plays to the additional devices.</summary>
public interface IAudioEngine : IDisposable
{
    bool IsRunning { get; }
    string? MasterId { get; }
    bool ClicksEnabled { get; set; }
    IReadOnlyCollection<string> ActiveOutputs { get; }

    /// <summary>Starts capture and the keep-alive signal on <paramref name="masterId"/>, then every wanted copy.</summary>
    void Start(string masterId);

    /// <summary>Moves capture to a new main device; it stops being a copy, and the old main becomes one if wanted.</summary>
    void ChangeMaster(string masterId);

    void Stop();

    /// <summary>Adds a copy or updates one; remembered while stopped, ignored for the main device.</summary>
    void SetOutput(string deviceId, OutputSettings settings);

    void RemoveOutput(string deviceId);

    /// <summary>A copy (or the capture, under the main device's ID) failed; raised on an audio thread.</summary>
    event Action<OutputFault>? OutputFaulted;
}

public sealed class AudioEngine : IAudioEngine
{
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

    private readonly IAudioBackend _backend;
    private readonly object _sync = new();
    private readonly Dictionary<string, OutputSettings> _wanted = new();
    private readonly Timer _logTimer;
    private ImmutableDictionary<string, Output> _active = ImmutableDictionary<string, Output>.Empty;
    private ICaptureSource? _capture;
    private IRenderSink? _masterSink;
    private MasterSignal? _masterSignal;
    private float[] _stereo = [];
    private bool _restartedCapture;
    private volatile bool _clicksEnabled;

    public AudioEngine(IAudioBackend backend)
    {
        _backend = backend;
        _logTimer = new Timer(_ => LogDelays(), null, LogInterval, LogInterval);
    }

    public event Action<OutputFault>? OutputFaulted;

    public bool IsRunning { get; private set; }

    public string? MasterId { get; private set; }

    public bool ClicksEnabled
    {
        get => _clicksEnabled;
        set
        {
            _clicksEnabled = value;
            var signal = _masterSignal;
            if (signal is not null) signal.ClicksEnabled = value;
        }
    }

    public IReadOnlyCollection<string> ActiveOutputs => Volatile.Read(ref _active).Keys.ToArray();

    public void Start(string masterId)
    {
        lock (_sync)
        {
            if (IsRunning) StopCore();
            MasterId = masterId;
            IsRunning = true;
            _restartedCapture = false;
            StartMaster();
            StartWantedOutputs();
        }
    }

    public void ChangeMaster(string masterId)
    {
        lock (_sync)
        {
            if (!IsRunning)
            {
                MasterId = masterId;
                return;
            }
            if (masterId == MasterId) return;

            StopMaster();
            RemoveActive(masterId);
            MasterId = masterId;
            _restartedCapture = false;
            StartMaster();
            StartWantedOutputs();
        }
    }

    public void Stop()
    {
        lock (_sync) StopCore();
    }

    public void SetOutput(string deviceId, OutputSettings settings)
    {
        lock (_sync)
        {
            _wanted[deviceId] = settings;
            if (!IsRunning || deviceId == MasterId) return;

            if (_active.TryGetValue(deviceId, out var output))
            {
                output.Stream.OffsetMs = settings.OffsetMs;
                output.Stream.Volume = settings.Volume;
            }
            else
            {
                StartOutput(deviceId, settings);
            }
        }
    }

    public void RemoveOutput(string deviceId)
    {
        lock (_sync)
        {
            _wanted.Remove(deviceId);
            RemoveActive(deviceId);
        }
    }

    public void Dispose()
    {
        Stop();
        _logTimer.Dispose();
    }

    private void StopCore()
    {
        foreach (var id in _active.Keys) RemoveActive(id);
        StopMaster();
        IsRunning = false;
    }

    private void StartMaster()
    {
        var masterId = MasterId!;
        try
        {
            StartCapture(masterId);
            _masterSink = _backend.CreateRender(masterId);
            _masterSignal = new MasterSignal(_masterSink.MixFormat) { ClicksEnabled = _clicksEnabled };
            _masterSink.Start(_masterSignal);
        }
        catch (Exception e)
        {
            // Copies still work without the keep-alive signal as long as capture runs.
            OutputFaulted?.Invoke(new OutputFault(masterId, e.Message));
        }
    }

    private void StartCapture(string masterId)
    {
        var previousRate = _capture?.Format.SampleRate;
        var capture = _backend.CreateLoopback(masterId);
        capture.Captured += OnCaptured;
        capture.Stopped += error => OnCaptureStopped(capture, error);
        _capture = capture;
        capture.Start();

        // A copy's input rate is fixed when it is created, so copies follow a capture whose rate changed.
        if (previousRate is not null && previousRate != capture.Format.SampleRate)
        {
            foreach (var id in _active.Keys)
            {
                RemoveActive(id);
                if (_wanted.TryGetValue(id, out var settings)) StartOutput(id, settings);
            }
        }
    }

    private void StopMaster()
    {
        if (_capture is { } capture)
        {
            _capture = null;
            capture.Captured -= OnCaptured;
            capture.Dispose();
        }
        _masterSink?.Dispose();
        _masterSink = null;
        _masterSignal = null;
    }

    private void StartWantedOutputs()
    {
        foreach (var (id, settings) in _wanted)
        {
            if (id != MasterId && !_active.ContainsKey(id)) StartOutput(id, settings);
        }
    }

    private void StartOutput(string deviceId, OutputSettings settings)
    {
        if (_capture is null) return;
        IRenderSink? sink = null;
        try
        {
            sink = _backend.CreateRender(deviceId);
            var stream = new CopyStream(_capture.Format.SampleRate, sink.MixFormat, settings.OffsetMs) { Volume = settings.Volume };
            var output = new Output(sink, stream);
            sink.Stopped += error => OnOutputStopped(deviceId, output, error);
            ImmutableInterlocked.Update(ref _active, a => a.SetItem(deviceId, output));
            sink.Start(stream);
        }
        catch (Exception e)
        {
            ImmutableInterlocked.Update(ref _active, a => a.Remove(deviceId));
            sink?.Dispose();
            OutputFaulted?.Invoke(new OutputFault(deviceId, e.Message));
        }
    }

    private void RemoveActive(string deviceId)
    {
        if (ImmutableInterlocked.TryRemove(ref _active, deviceId, out var output)) output.Sink.Dispose();
    }

    private void OnCaptured(ReadOnlyMemory<float> samples)
    {
        var capture = _capture;
        if (capture is null) return;
        _restartedCapture = false;

        var channels = capture.Format.Channels;
        var frames = samples.Length / channels;
        if (_stereo.Length < frames * 2) _stereo = new float[frames * 2];
        var stereo = _stereo.AsSpan(0, frames * 2);
        ChannelMapper.ToStereo(samples.Span[..(frames * channels)], channels, stereo);

        foreach (var output in Volatile.Read(ref _active).Values) output.Stream.Write(stereo);
    }

    private void OnCaptureStopped(ICaptureSource capture, Exception? error)
    {
        if (error is not null) Task.Run(() => HandleCaptureFailure(capture, error));
    }

    private void HandleCaptureFailure(ICaptureSource capture, Exception error)
    {
        lock (_sync)
        {
            if (capture != _capture) return;
            var masterId = MasterId!;
            capture.Captured -= OnCaptured;
            capture.Dispose();
            _capture = null;

            if (!_restartedCapture)
            {
                _restartedCapture = true;
                try
                {
                    StartCapture(masterId);
                    return;
                }
                catch (Exception restartError)
                {
                    error = restartError;
                }
            }

            StopCore();
            OutputFaulted?.Invoke(new OutputFault(masterId, error.Message));
        }
    }

    // Failures arrive on the device's own thread, which a Dispose under _sync may be waiting for;
    // handling them on the thread pool keeps that thread from blocking on _sync.
    private void OnOutputStopped(string deviceId, Output output, Exception? error)
    {
        if (error is null) return;
        Task.Run(() =>
        {
            lock (_sync)
            {
                if (!_active.TryGetValue(deviceId, out var current) || current != output) return;
                ImmutableInterlocked.TryRemove(ref _active, deviceId, out _);
                _wanted.Remove(deviceId);
            }
            output.Sink.Dispose();
            OutputFaulted?.Invoke(new OutputFault(deviceId, error.Message));
        });
    }

    private void LogDelays()
    {
        foreach (var (id, output) in Volatile.Read(ref _active))
            Debug.WriteLine($"[MultiSound] {id}: buffered {output.Stream.BufferedMs:F1} ms, target {output.Stream.TargetMs:F1} ms");
    }

    private sealed record Output(IRenderSink Sink, CopyStream Stream);
}
