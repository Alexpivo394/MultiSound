using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MultiSound.Audio.Backend;

/// <summary>WASAPI shared-mode capture and playback through NAudio.</summary>
public sealed class WasapiBackend : IAudioBackend, IDisposable
{
    private const int RenderLatencyMs = 30;

    private readonly MMDeviceEnumerator _enumerator = new();

    public ICaptureSource CreateLoopback(string deviceId) => new LoopbackSource(_enumerator.GetDevice(deviceId));

    public IRenderSink CreateRender(string deviceId) => new RenderSink(_enumerator.GetDevice(deviceId));

    public void Dispose() => _enumerator.Dispose();

    private static bool IsFloat32(WaveFormat format) =>
        format.BitsPerSample == 32 && (format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT));

    private sealed class LoopbackSource : ICaptureSource
    {
        private readonly MMDevice _device;
        private readonly LowLatencyLoopbackCapture _capture;
        private float[] _samples = [];

        public LoopbackSource(MMDevice device)
        {
            _device = device;
            _capture = new LowLatencyLoopbackCapture(device);
            var format = _capture.WaveFormat;
            if (!IsFloat32(format))
            {
                _capture.Dispose();
                device.Dispose();
                throw new NotSupportedException($"Формат захвата не поддерживается: {format}");
            }
            Format = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        }

        public WaveFormat Format { get; }

        public event Action<ReadOnlyMemory<float>>? Captured;
        public event Action<Exception?>? Stopped;

        public void Start() => _capture.StartRecording();

        public void Dispose()
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _device.Dispose();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            var count = e.BytesRecorded / sizeof(float);
            if (count == 0) return;
            if (_samples.Length < count) _samples = new float[count];
            MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, count * sizeof(float))).CopyTo(_samples);
            Captured?.Invoke(_samples.AsMemory(0, count));
        }
    }

    private sealed class RenderSink : IRenderSink
    {
        private readonly MMDevice _device;
        private WasapiOut? _out;

        public RenderSink(MMDevice device)
        {
            _device = device;
            var mix = device.AudioClient.MixFormat;
            MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
        }

        public WaveFormat MixFormat { get; }

        public event Action<Exception?>? Stopped;

        public void Start(ISampleProvider source)
        {
            _out = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, RenderLatencyMs);
            _out.PlaybackStopped += (_, e) => Stopped?.Invoke(e.Exception);
            _out.Init(new SampleToWaveProvider(source));
            _out.Play();
        }

        public void Dispose()
        {
            _out?.Dispose();
            _device.Dispose();
        }
    }
}

/// <summary>
/// Loopback capture with a 20 ms buffer. NAudio's <see cref="WasapiLoopbackCapture"/> uses 100 ms and polls every
/// 50 ms, so audio arrives in ~60 ms packets — more than a copy's safety margin, which starved the copies.
/// </summary>
internal sealed class LowLatencyLoopbackCapture(MMDevice device)
    : WasapiCapture(device, useEventSync: false, audioBufferMillisecondsLength: BufferMs)
{
    private const int BufferMs = 20;

    protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
}
