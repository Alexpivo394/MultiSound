using NAudio.Wave;

namespace MultiSound.Audio;

/// <summary>
/// Endless stream played on the main device: silence that keeps loopback capture running through pauses,
/// or, while <see cref="ClicksEnabled"/>, a short click once a second for calibrating offsets by ear.
/// </summary>
/// <remarks>Loopback capture picks the clicks up, so they reach every copy through the same path as the film.</remarks>
public sealed class MasterSignal : ISampleProvider
{
    // 15 ms is long enough to hear on weak speakers next to in-ear headphones; the 1 ms attack keeps the start sharp.
    private const double ClickSeconds = 0.015;
    private const double AttackSeconds = 0.001;
    private const double ClickFrequency = 1000;
    private const float ClickAmplitude = 0.8f;

    private readonly float[] _click;
    private readonly int _period;
    private volatile bool _clicksEnabled;
    private volatile bool _restart;
    private long _position;

    public MasterSignal(WaveFormat format)
    {
        WaveFormat = format;
        _period = format.SampleRate;

        // Envelope: a 1 ms rise to the peak, then an exponential decay that a short linear taper brings to zero.
        var length = (int)Math.Round(ClickSeconds * format.SampleRate);
        var attack = (int)Math.Round(AttackSeconds * format.SampleRate);
        var taper = length / 10;
        _click = new float[length];
        for (var n = 0; n < length; n++)
        {
            var envelope = n < attack
                ? Math.Pow(Math.Sin(Math.PI / 2 * (n + 1) / attack), 2)
                : Math.Exp(-5.0 * (n - attack) / (length - attack));
            if (n >= length - taper) envelope *= (length - n) / (double)taper;
            var tone = Math.Cos(2 * Math.PI * ClickFrequency * (n + 1 - attack) / format.SampleRate);
            _click[n] = (float)(ClickAmplitude * envelope * tone);
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
