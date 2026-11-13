namespace MultiSound.Tests;

internal static class Signals
{
    /// <summary>Interleaved stereo sine, same in both channels.</summary>
    public static float[] StereoSine(double frequency, int sampleRate, int frames, double amplitude = 1.0, long startFrame = 0)
    {
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * (startFrame + i) / sampleRate));
            data[2 * i] = v;
            data[2 * i + 1] = v;
        }
        return data;
    }

    public static float[] StereoRamp(int frames)
    {
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            data[2 * i] = i + 1;
            data[2 * i + 1] = -(i + 1);
        }
        return data;
    }

    /// <summary>Largest jump between adjacent samples of one channel.</summary>
    public static float MaxAdjacentDiff(ReadOnlySpan<float> interleaved, int channels, int channel = 0)
    {
        var max = 0f;
        for (var i = channels + channel; i < interleaved.Length; i += channels)
            max = Math.Max(max, Math.Abs(interleaved[i] - interleaved[i - channels]));
        return max;
    }

    /// <summary>Frequency estimated from rising zero crossings of one channel.</summary>
    public static double Frequency(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        int first = -1, last = -1, count = 0;
        var frames = interleaved.Length / channels;
        for (var i = 1; i < frames; i++)
        {
            if (interleaved[(i - 1) * channels] < 0 && interleaved[i * channels] >= 0)
            {
                if (first < 0) first = i; else count++;
                last = i;
            }
        }
        return count == 0 ? 0 : count * (double)sampleRate / (last - first);
    }
}
