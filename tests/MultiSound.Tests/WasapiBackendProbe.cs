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

    /// <summary>Compares the tone level played on the main device with what capture and the copy device carry.</summary>
    [Test]
    [Explicit]
    public async Task LevelsAndClicks()
    {
        using var catalog = new DeviceCatalog();
        using var backend = new WasapiBackend();
        using var engine = new AudioEngine(backend);
        var master = catalog.GetDefaultId()!;
        var other = catalog.GetRenderDevices().First(d => d.Id != master);
        using var enumerator = new MMDeviceEnumerator();
        var masterDevice = enumerator.GetDevice(master);
        var otherDevice = enumerator.GetDevice(other.Id);
        Console.WriteLine($"main: {masterDevice.FriendlyName}; copy: {otherDevice.FriendlyName}; copy volume support: {otherDevice.AudioEndpointVolume.HardwareSupport}");
        Console.WriteLine($"master volume {masterDevice.AudioEndpointVolume.MasterVolumeLevelScalar:F2} mute {masterDevice.AudioEndpointVolume.Mute}");
        Console.WriteLine($"master volume dB {masterDevice.AudioEndpointVolume.MasterVolumeLevel:F2} → x{Math.Pow(10, masterDevice.AudioEndpointVolume.MasterVolumeLevel / 20):F3}; hardware support {masterDevice.AudioEndpointVolume.HardwareSupport}");
        Console.WriteLine($"copy volume {otherDevice.AudioEndpointVolume.MasterVolumeLevelScalar:F2} mute {otherDevice.AudioEndpointVolume.Mute}");

        // Peak of the master loopback while a 0.1 tone plays.
        using var tonePlayer = new WasapiOut(enumerator.GetDevice(master), AudioClientShareMode.Shared, true, 50);
        tonePlayer.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.1 });
        tonePlayer.Play();
        Console.WriteLine($"master loopback peak with 0.1 tone: {await Peak(enumerator.GetDevice(master), 1500):F4}");

        engine.Start(master);
        engine.SetOutput(other.Id, new OutputSettings(1f, 0));
        await Task.Delay(1500);
        Console.WriteLine($"copy loopback peak with 0.1 tone: {await Peak(enumerator.GetDevice(other.Id), 1500):F4}");
        tonePlayer.Stop();

        engine.ClicksEnabled = true;
        await Task.Delay(500);
        Console.WriteLine($"master loopback peak with clicks: {await Peak(enumerator.GetDevice(master), 2500):F4}");
        var copyClicks = await Peak(enumerator.GetDevice(other.Id), 2500);
        Console.WriteLine($"copy loopback peak with clicks: {copyClicks:F4}");
        await Assert.That(copyClicks).IsGreaterThan(0f);
    }

    /// <summary>Measures how the main device's Windows volume scales its loopback (restores the volume afterwards).</summary>
    [Test]
    [Explicit]
    public async Task LoopbackFollowsEndpointVolume()
    {
        using var catalog = new DeviceCatalog();
        using var enumerator = new MMDeviceEnumerator();
        var master = enumerator.GetDevice(catalog.GetDefaultId()!);
        var volume = master.AudioEndpointVolume;
        Console.WriteLine($"main device: {master.FriendlyName}; volume support: {volume.HardwareSupport}");
        var original = volume.MasterVolumeLevelScalar;
        var amplitudes = new List<double>();

        using var tonePlayer = new WasapiOut(enumerator.GetDevice(master.ID), AudioClientShareMode.Shared, true, 50);
        tonePlayer.Init(new SignalGenerator(48000, 2) { Frequency = 7000, Gain = 0.05 });
        tonePlayer.Play();
        try
        {
            foreach (var scalar in new[] { 0.3f, 0.6f, 1.0f })
            {
                volume.MasterVolumeLevelScalar = scalar;
                await Task.Delay(300);
                var amplitude = await ToneAmplitude(enumerator.GetDevice(master.ID), 7000, 1000);
                amplitudes.Add(amplitude);
                Console.WriteLine($"scalar {scalar:F2} dB {volume.MasterVolumeLevel:F2} (x{Math.Pow(10, volume.MasterVolumeLevel / 20):F3}) → tone {amplitude:F4} (x{amplitude / 0.05:F3})");
            }
        }
        finally
        {
            volume.MasterVolumeLevelScalar = original;
            tonePlayer.Stop();
        }
        await Assert.That(amplitudes.Count).IsEqualTo(3);
    }

    private static async Task<double> ToneAmplitude(MMDevice device, double frequency, int milliseconds)
    {
        var samples = new List<float>();
        using var recorder = new WasapiLoopbackCapture(device);
        var channels = recorder.WaveFormat.Channels;
        var rate = recorder.WaveFormat.SampleRate;
        recorder.DataAvailable += (_, e) =>
        {
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            lock (samples) for (var i = 0; i < floats.Length; i += channels) samples.Add(floats[i]);
        };
        recorder.StartRecording();
        await Task.Delay(milliseconds);
        recorder.StopRecording();
        float[] x;
        lock (samples) x = samples.ToArray();
        // Goertzel: amplitude of one frequency, unaffected by other sounds playing.
        double re = 0, im = 0;
        for (var n = 0; n < x.Length; n++)
        {
            var phase = 2 * Math.PI * frequency * n / rate;
            re += x[n] * Math.Cos(phase);
            im += x[n] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(re * re + im * im) / x.Length;
    }

    private static async Task<float> Peak(MMDevice device, int milliseconds)
    {
        var peak = 0f;
        using var recorder = new WasapiLoopbackCapture(device);
        recorder.DataAvailable += (_, e) =>
        {
            foreach (var v in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded)))
                peak = Math.Max(peak, Math.Abs(v));
        };
        recorder.StartRecording();
        await Task.Delay(milliseconds);
        recorder.StopRecording();
        return peak;
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
