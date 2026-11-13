using MultiSound.Audio.Backend;
using NAudio.Wave;

namespace MultiSound.Tests.Fakes;

internal sealed class FakeBackend : IAudioBackend
{
    public List<FakeCapture> Captures { get; } = [];
    public List<FakeRender> Renders { get; } = [];
    public HashSet<string> FailingDevices { get; } = [];
    public Dictionary<string, int> CaptureRates { get; } = new();

    public FakeCapture LastCapture => Captures[^1];

    public IEnumerable<FakeRender> Live(string deviceId) => Renders.Where(r => r.DeviceId == deviceId && !r.Disposed);

    public ICaptureSource CreateLoopback(string deviceId)
    {
        var capture = new FakeCapture(deviceId, CaptureRates.GetValueOrDefault(deviceId, 48000));
        Captures.Add(capture);
        return capture;
    }

    public IRenderSink CreateRender(string deviceId)
    {
        if (FailingDevices.Contains(deviceId)) throw new InvalidOperationException($"Device {deviceId} is gone");
        var render = new FakeRender(deviceId);
        Renders.Add(render);
        return render;
    }
}

internal sealed class FakeCapture(string deviceId, int sampleRate = 48000) : ICaptureSource
{
    public string DeviceId { get; } = deviceId;
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public WaveFormat Format { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

    public event Action<ReadOnlyMemory<float>>? Captured;
    public event Action<Exception?>? Stopped;

    public void Start() => Started = true;
    public void Dispose() => Disposed = true;

    public void Push(float[] interleaved) => Captured?.Invoke(interleaved);
    public void Fail(Exception error) => Stopped?.Invoke(error);
}

internal sealed class FakeRender(string deviceId) : IRenderSink
{
    public string DeviceId { get; } = deviceId;
    public ISampleProvider? Source { get; private set; }
    public bool Disposed { get; private set; }
    public WaveFormat MixFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public event Action<Exception?>? Stopped;

    public Action? OnDispose { get; set; }

    public void Start(ISampleProvider source) => Source = source;

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        OnDispose?.Invoke();
    }

    public float[] Pull(int frames)
    {
        var buffer = new float[frames * MixFormat.Channels];
        Source!.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    public void Fail(Exception error) => Stopped?.Invoke(error);
}
