using System.Diagnostics;
using NAudio.Wave;

namespace MultiSound.Audio;

/// <summary>
/// Audio for one additional device: delays the captured stereo stream by <see cref="OffsetMs"/>, follows the
/// capture clock, and renders in the device's own format.
/// </summary>
/// <remarks><see cref="Write"/> runs on the capture thread and <see cref="Read"/> on the device's render thread.</remarks>
public sealed class CopyStream : ISampleProvider
{
    public const double SafetyMs = 20;
    public const double JumpThresholdMs = 50;
    public const double FadeMs = 5;
    public const double MaxOffsetMs = 500;

    // Time since the last capture block counts toward the delay only up to this much; a longer gap is a stall.
    private const double MaxSinceWriteMs = 30;

    private readonly int _inputRate;
    private readonly Func<double> _clockSeconds;
    private readonly DelayBuffer _buffer;
    private readonly VariableRateResampler _resampler = new();
    private readonly DriftController _controller = new();
    private float[] _stereo = [];
    private double _offsetMs;
    private float _volume = 1f;
    private double _lastWriteSeconds = double.NaN;
    private bool _priming = true;
    private readonly LowPassFilter? _antiAlias;
    private float[] _filtered = [];

    // Fraction of the output rate kept by the anti-alias filter; just under its Nyquist frequency.
    private const double AntiAliasCutoff = 0.45;

    /// <param name="inputSampleRate">Sample rate of the captured stereo stream.</param>
    /// <param name="outputFormat">IEEE float format of the device.</param>
    /// <param name="offsetMs">Initial user offset.</param>
    /// <param name="clockSeconds">Monotonic clock; a <see cref="Stopwatch"/> by default.</param>
    public CopyStream(int inputSampleRate, WaveFormat outputFormat, double offsetMs, Func<double>? clockSeconds = null)
    {
        _inputRate = inputSampleRate;
        WaveFormat = outputFormat;
        OffsetMs = offsetMs;
        _clockSeconds = clockSeconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _buffer = new DelayBuffer(inputSampleRate * 2);

        // Going to a lower rate (44.1 kHz, a 16 kHz hands-free headset) needs a low-pass first, or highs fold back as noise.
        if (outputFormat.SampleRate < inputSampleRate * 0.99)
            _antiAlias = new LowPassFilter(AntiAliasCutoff * outputFormat.SampleRate / inputSampleRate);
    }

    public WaveFormat WaveFormat { get; }

    public int InputSampleRate => _inputRate;

    public double OffsetMs
    {
        get => Volatile.Read(ref _offsetMs);
        set => Volatile.Write(ref _offsetMs, Math.Clamp(value, 0, MaxOffsetMs));
    }

    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 1f));
    }

    public double TargetMs => SafetyMs + OffsetMs;

    /// <summary>
    /// Current delay of the copy behind the capture: buffered audio plus the time since the last capture block,
    /// so the value does not depend on how capture and render blocks happen to interleave.
    /// </summary>
    public double BufferedMs
    {
        get
        {
            var buffered = (_buffer.Count + _resampler.PendingFrames) * 1000.0 / _inputRate;
            var last = Volatile.Read(ref _lastWriteSeconds);
            if (double.IsNaN(last)) return buffered;
            return buffered + Math.Clamp((_clockSeconds() - last) * 1000.0, 0, MaxSinceWriteMs);
        }
    }

    public void Write(ReadOnlySpan<float> stereo)
    {
        if (_antiAlias is not null)
        {
            // The capture block is shared by every copy, so filter a private copy of it.
            if (_filtered.Length < stereo.Length) _filtered = new float[stereo.Length];
            var filtered = _filtered.AsSpan(0, stereo.Length);
            stereo.CopyTo(filtered);
            _antiAlias.Process(filtered);
            stereo = filtered;
        }
        _buffer.Write(stereo);
        Volatile.Write(ref _lastWriteSeconds, _clockSeconds());
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = WaveFormat.Channels;
        var frames = count / channels;
        var target = TargetMs;
        var fill = BufferedMs;

        if (_priming)
        {
            // After a start or a dropout, wait in silence until the buffer holds the target again.
            if (fill < target)
            {
                buffer.AsSpan(offset, count).Clear();
                return count;
            }
            _buffer.Skip(ToFrames(fill - target), 0);
            _controller.Reset();
            _priming = false;
            fill = BufferedMs;
        }

        var error = fill - target;
        if (Math.Abs(error) > JumpThresholdMs)
        {
            var errorFrames = ToFrames(Math.Abs(error));
            var fadeFrames = ToFrames(FadeMs);
            if (error > 0) _buffer.Skip(errorFrames, fadeFrames);
            else _buffer.InsertSilence(errorFrames, fadeFrames);
            _controller.Reset();
            fill = BufferedMs;
        }

        var dt = (double)frames / WaveFormat.SampleRate;
        var step = (double)_inputRate / WaveFormat.SampleRate * (1 + _controller.Update(fill, target, dt));

        if (_stereo.Length < frames * 2) _stereo = new float[frames * 2];
        var stereo = _stereo.AsSpan(0, frames * 2);
        var produced = _resampler.Render(_buffer, stereo, step);
        if (produced < frames)
        {
            stereo[(produced * 2)..].Clear();
            _priming = true;
        }

        var volume = Volume;
        if (volume != 1f)
        {
            foreach (ref var sample in stereo) sample *= volume;
        }

        ChannelMapper.FromStereo(stereo, buffer.AsSpan(offset, frames * channels), channels);
        buffer.AsSpan(offset + frames * channels, count - frames * channels).Clear();
        return count;
    }

    private int ToFrames(double ms) => (int)Math.Round(ms * _inputRate / 1000.0);
}
