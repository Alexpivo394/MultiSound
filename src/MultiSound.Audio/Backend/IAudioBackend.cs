using NAudio.Wave;

namespace MultiSound.Audio.Backend;

/// <summary>Loopback capture of everything a device plays.</summary>
public interface ICaptureSource : IDisposable
{
    /// <summary>IEEE float format of <see cref="Captured"/> data: the device's sample rate and channels.</summary>
    WaveFormat Format { get; }

    /// <summary>Interleaved samples in <see cref="Format"/>; raised on the capture thread. The memory is reused after the handler returns.</summary>
    event Action<ReadOnlyMemory<float>>? Captured;

    /// <summary>Capture ended; the exception is null for a requested stop.</summary>
    event Action<Exception?>? Stopped;

    void Start();
}

/// <summary>Playback on one device.</summary>
public interface IRenderSink : IDisposable
{
    /// <summary>IEEE float format matching the device's shared-mode mix format.</summary>
    WaveFormat MixFormat { get; }

    /// <summary>Playback ended; the exception is null for a requested stop.</summary>
    event Action<Exception?>? Stopped;

    void Start(ISampleProvider source);
}

public interface IAudioBackend
{
    ICaptureSource CreateLoopback(string deviceId);
    IRenderSink CreateRender(string deviceId);
}
