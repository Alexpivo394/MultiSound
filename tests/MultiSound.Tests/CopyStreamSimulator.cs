using MultiSound.Audio;

namespace MultiSound.Tests;

/// <summary>Drives a <see cref="CopyStream"/> in virtual time: 10 ms capture blocks against 10 ms render blocks.</summary>
internal sealed class CopyStreamSimulator(CopyStream stream, int inputRate, VirtualClock clock, double drift = 0)
{
    private readonly int _outRate = stream.WaveFormat.SampleRate;
    private readonly int _channels = stream.WaveFormat.Channels;
    private double _nextWrite;
    private double _nextRead;
    private long _inputFrame;

    public double Time { get; private set; }

    /// <summary>Stereo input for frames [start, start + count); null means silence.</summary>
    public Func<long, int, float[]>? Source { get; set; }

    /// <summary>When false at a given time, capture delivers nothing.</summary>
    public Func<double, bool> WriteEnabled { get; set; } = _ => true;

    /// <param name="onBeforeRead">Gets <see cref="CopyStream.BufferedMs"/> just before each render block.</param>
    public void Run(double seconds, Action<float[]>? onRead = null, Action<double>? onBeforeRead = null)
    {
        var end = Time + seconds;
        var writeFrames = inputRate / 100;
        var readSamples = _outRate / 100 * _channels;
        var silence = new float[writeFrames * 2];
        var output = new float[readSamples];
        var writeInterval = 0.01 / (1 + drift);

        while (true)
        {
            if (_nextWrite <= _nextRead)
            {
                if (_nextWrite >= end) break;
                Time = clock.Seconds = _nextWrite;
                if (WriteEnabled(Time))
                {
                    stream.Write(Source?.Invoke(_inputFrame, writeFrames) ?? silence);
                    _inputFrame += writeFrames;
                }
                _nextWrite += writeInterval;
            }
            else
            {
                if (_nextRead >= end) break;
                Time = clock.Seconds = _nextRead;
                onBeforeRead?.Invoke(stream.BufferedMs);
                stream.Read(output, 0, output.Length);
                onRead?.Invoke(output);
                _nextRead += 0.01;
            }
        }
        Time = end;
    }

    /// <summary>Runs and returns the means of consecutive one-second windows of pre-read fill.</summary>
    public List<double> WindowMeans(double seconds, Action<float[]>? onRead = null)
    {
        var means = new List<double>();
        double sum = 0;
        var n = 0;
        Run(seconds, onRead, ms =>
        {
            sum += ms;
            if (++n == 100)
            {
                means.Add(sum / n);
                sum = 0;
                n = 0;
            }
        });
        return means;
    }

    public static Func<long, int, float[]> Sine(double frequency, int rate, double amplitude = 1.0) =>
        (start, count) => Signals.StereoSine(frequency, rate, count, amplitude, start);
}

internal sealed class VirtualClock
{
    public double Seconds { get; set; }
}
