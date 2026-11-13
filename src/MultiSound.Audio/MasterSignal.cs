using NAudio.Wave;

namespace MultiSound.Audio;

/// <summary>
/// Endless stream played on the main device: silence that keeps loopback capture running through pauses,
/// or, while <see cref="ClicksEnabled"/>, a short click once a second for calibrating offsets by ear.
/// </summary>
/// <remarks>Loopback capture picks the clicks up, so they reach every copy through the same path as the film.</remarks>
public sealed class MasterSignal : ISampleProvider
{
    private const double ClickSeconds = 0.005;
    private const double ClickFrequency = 1000;
    private const float ClickAmplitude = 0.5f;

    private readonly float[] _click;
    private readonly int _period;
    private volatile bool _clicksEnabled;
    private volatile bool _restart;
    private long _position;

    public MasterSignal(WaveFormat format)
    {
        WaveFormat = format;
        _period = format.SampleRate;

        var length = (int)Math.Round(ClickSeconds * format.SampleRate);
        var center = (length + 1) / 2.0;
        _click = new float[length];
        for (var n = 1; n <= length; n++)
        {
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * n / (length + 1)));
            var tone = Math.Cos(2 * Math.PI * ClickFrequency * (n - center) / format.SampleRate);
            _click[n - 1] = (float)(ClickAmplitude * window * tone);
        }
    }

    public WaveFormat WaveFormat { get; }

    public bool ClicksEnabled
    {
        get => _clicksEnabled;
        set
        {
            if (value && !_clicksEnabled) _restart = true;
            _clicksEnabled = value;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var span = buffer.AsSpan(offset, count);
        span.Clear();
        if (!_clicksEnabled) return count;

        if (_restart)
        {
            _restart = false;
            _position = 0;
        }

        var channels = WaveFormat.Channels;
        var frames = count / channels;
        for (var i = 0; i < frames; i++)
        {
            var inPeriod = (int)((_position + i) % _period);
            if (inPeriod >= _click.Length) continue;
            span.Slice(i * channels, channels).Fill(_click[inPeriod]);
        }
        _position += frames;
        return count;
    }
}
