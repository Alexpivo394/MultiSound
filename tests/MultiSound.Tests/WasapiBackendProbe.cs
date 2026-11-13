using MultiSound.Audio;
using MultiSound.Audio.Backend;
using MultiSound.Audio.Devices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MultiSound.Tests;

/// <summary>Manual checks on real devices. Run with --treenode-filter "/*/*/WasapiBackendProbe/*".</summary>
public class WasapiBackendProbe
{
    [Test]
    [Explicit]
    public async Task EngineRunsOnRealDevices()
    {
        using var catalog = new DeviceCatalog();
        using var backend = new WasapiBackend();
        using var engine = new AudioEngine(backend);
        var faults = new List<OutputFault>();
        engine.OutputFaulted += faults.Add;

        var master = catalog.GetDefaultId()!;
        var other = catalog.GetRenderDevices().First(d => d.Id != master);
        engine.Start(master);
        engine.SetOutput(other.Id, new OutputSettings(1f, 100));
        await Task.Delay(3000);

        Console.WriteLine($"active: {string.Join(", ", engine.ActiveOutputs)}; faults: {string.Join("; ", faults)}");
        await Assert.That(faults).IsEmpty();
        await Assert.That(engine.ActiveOutputs).Contains(other.Id);
    }

    /// <summary>Plays a quiet tone on the main device for 8 s and prints the copy's render statistics.</summary>
    [Test]
    [Explicit]
    [Arguments(0)]
    [Arguments(100)]
    public async Task CopyStatsWithTone(int offsetMs)
    {
        using var catalog = new DeviceCatalog();
        using var backend = new WasapiBackend();
        var spy = new SpyBackend(backend);
        using var engine = new AudioEngine(spy);

        var master = catalog.GetDefaultId()!;
        var other = catalog.GetRenderDevices().First(d => d.Id != master);

        using var enumerator = new MMDeviceEnumerator();
        using var tonePlayer = new WasapiOut(enumerator.GetDevice(master), AudioClientShareMode.Shared, true, 50);
        tonePlayer.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.02 });
        tonePlayer.Play();

        engine.Start(master);
        engine.SetOutput(other.Id, new OutputSettings(1f, offsetMs));
        var stream = spy.Streams[other.Id];
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(1000);
            Console.WriteLine($"t={i + 1}s buffered={stream.BufferedMs:F1} target={stream.TargetMs} stats={stream.Stats}");
        }
        tonePlayer.Stop();
        await Assert.That(stream.Stats.Reads).IsGreaterThan(0);
    }

    /// <summary>Records what the copy device actually plays and counts 10 ms windows where the tone dropped out.</summary>
    [Test]
    [Explicit]
    public async Task CopyOutputIsContinuous()
    {
        using var catalog = new DeviceCatalog();
        using var backend = new WasapiBackend();
        using var engine = new AudioEngine(backend);
        var master = catalog.GetDefaultId()!;
        var other = catalog.GetRenderDevices().First(d => d.Id != master);

        using var enumerator = new MMDeviceEnumerator();
        using var tonePlayer = new WasapiOut(enumerator.GetDevice(master), AudioClientShareMode.Shared, true, 50);
        tonePlayer.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.02 });
        tonePlayer.Play();
        engine.Start(master);
        engine.SetOutput(other.Id, new OutputSettings(1f, 0));
        await Task.Delay(2000);

        var samples = new List<float>();
        using var recorder = new WasapiLoopbackCapture(enumerator.GetDevice(other.Id));
        var channels = recorder.WaveFormat.Channels;
        var rate = recorder.WaveFormat.SampleRate;
        recorder.DataAvailable += (_, e) =>
        {
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            lock (samples)
                for (var i = 0; i < floats.Length; i += channels) samples.Add(floats[i]);
        };
        recorder.StartRecording();
        await Task.Delay(6000);
        recorder.StopRecording();
        tonePlayer.Stop();

        float[] mono;
        lock (samples) mono = samples.ToArray();
        var window = rate / 100;
        var windows = mono.Length / window;
        var silent = 0;
        for (var w = 0; w < windows; w++)
        {
            double sum = 0;
            for (var i = 0; i < window; i++) sum += mono[w * window + i] * mono[w * window + i];
            if (Math.Sqrt(sum / window) < 0.005) silent++;
        }
        Console.WriteLine($"recorded {mono.Length / (double)rate:F1} s, {windows} windows, silent {silent}, peak {mono.Max(Math.Abs):F4}");
        await Assert.That(silent).IsEqualTo(0);
    }

    /// <summary>Wraps the real backend to get hold of the CopyStream each sink plays.</summary>
    private sealed class SpyBackend(IAudioBackend inner) : IAudioBackend
    {
        public Dictionary<string, CopyStream> Streams { get; } = new();

        public ICaptureSource CreateLoopback(string deviceId) => new SpyCapture(inner.CreateLoopback(deviceId));

        public IRenderSink CreateRender(string deviceId) => new SpySink(inner.CreateRender(deviceId), deviceId, this);

        private sealed class SpyCapture(ICaptureSource capture) : ICaptureSource
        {
            private int _packets;
            private int _minSamples = int.MaxValue, _maxSamples;

            public WaveFormat Format => capture.Format;

            public event Action<ReadOnlyMemory<float>>? Captured
            {
                add => capture.Captured += m =>
                {
                    if (++_packets % 200 == 0)
                        Console.WriteLine($"capture: {_packets} packets, samples {_minSamples}..{_maxSamples}, format {capture.Format}");
                    _minSamples = Math.Min(_minSamples, m.Length);
                    _maxSamples = Math.Max(_maxSamples, m.Length);
                    value!(m);
                };
                remove { }
            }

            public event Action<Exception?>? Stopped
            {
                add => capture.Stopped += value;
                remove => capture.Stopped -= value;
            }

            public void Start() => capture.Start();
            public void Dispose() => capture.Dispose();
        }

        private sealed class SpySink(IRenderSink sink, string id, SpyBackend owner) : IRenderSink
        {
            public WaveFormat MixFormat => sink.MixFormat;

            public event Action<Exception?>? Stopped
            {
                add => sink.Stopped += value;
                remove => sink.Stopped -= value;
            }

            public void Start(ISampleProvider source)
            {
                if (source is CopyStream stream) owner.Streams[id] = stream;
                sink.Start(source);
            }

            public void Dispose() => sink.Dispose();
        }
    }
}
