namespace MultiSound.Audio;

/// <summary>Stereo resampler whose rate may change on every call, using cubic Hermite (Catmull-Rom) interpolation.</summary>
/// <remarks>Keeps its fractional position and the last four input frames between calls, so a change of
/// <c>step</c> never breaks the waveform.</remarks>
public sealed class VariableRateResampler
{
    // History frames x[-1], x[0], x[1], x[2] for each channel; output lies between x[0] and x[1].
    private readonly float[] _history = new float[8];
    private float[] _staging = new float[1024];
    private int _stagingStart;
    private int _stagingCount;
    private double _fraction;

    /// <summary>Frames taken from the source but not yet consumed.</summary>
    public int PendingFrames => _stagingCount;

    /// <summary>Renders up to <c>dstStereo.Length / 2</c> frames.</summary>
    /// <param name="step">Input frames per output frame.</param>
    /// <returns>Frames produced; fewer than requested when the source runs out.</returns>
    public int Render(DelayBuffer source, Span<float> dstStereo, double step)
    {
        var outFrames = dstStereo.Length / 2;
        Stage(source, (int)Math.Floor(_fraction + outFrames * step) + 1);

        // Finish an advance left pending by an earlier underflow.
        while (_fraction >= 1.0)
        {
            if (!Advance()) return 0;
            _fraction -= 1.0;
        }

        var produced = 0;
        while (produced < outFrames)
        {
            var t = (float)_fraction;
            for (var c = 0; c < 2; c++)
            {
                dstStereo[produced * 2 + c] = Interpolate(_history[c], _history[2 + c], _history[4 + c], _history[6 + c], t);
            }
            produced++;

            _fraction += step;
            var underflow = false;
            while (_fraction >= 1.0)
            {
                if (!Advance())
                {
                    underflow = true;
                    break;
                }
                _fraction -= 1.0;
            }
            if (underflow) break;
        }
        return produced;
    }

    public void Reset()
    {
        Array.Clear(_history);
        _stagingStart = 0;
        _stagingCount = 0;
        _fraction = 0;
    }

    private void Stage(DelayBuffer source, int wanted)
    {
        var missing = wanted - _stagingCount;
        if (missing <= 0) return;

        if (_staging.Length < wanted * 2)
        {
            var bigger = new float[wanted * 2 * 2];
            _staging.AsSpan(_stagingStart * 2, _stagingCount * 2).CopyTo(bigger);
            _staging = bigger;
            _stagingStart = 0;
        }
        else if (_stagingStart > 0)
        {
            _staging.AsSpan(_stagingStart * 2, _stagingCount * 2).CopyTo(_staging);
            _stagingStart = 0;
        }

        _stagingCount += source.Read(_staging.AsSpan(_stagingCount * 2, missing * 2));
    }

    private bool Advance()
    {
        if (_stagingCount == 0) return false;
        Array.Copy(_history, 2, _history, 0, 6);
        _history[6] = _staging[_stagingStart * 2];
        _history[7] = _staging[_stagingStart * 2 + 1];
        _stagingStart++;
        _stagingCount--;
        return true;
    }

    private static float Interpolate(float xm1, float x0, float x1, float x2, float t)
    {
        var c1 = 0.5f * (x1 - xm1);
        var c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
        var c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * t + c2) * t + c1) * t + x0;
    }
}
