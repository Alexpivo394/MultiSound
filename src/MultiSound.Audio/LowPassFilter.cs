namespace MultiSound.Audio;

/// <summary>Stereo windowed-sinc FIR low-pass, applied before downsampling so content above the new Nyquist does not fold back.</summary>
/// <remarks>Keeps its own history, so it filters a stream block by block.</remarks>
public sealed class LowPassFilter
{
    private const int Taps = 63;

    private readonly float[] _kernel = new float[Taps];
    private readonly float[] _history = new float[(Taps - 1) * 2];
    private float[] _work = [];

    /// <param name="cutoffRatio">Cutoff as a fraction of the sample rate, below 0.5.</param>
    public LowPassFilter(double cutoffRatio)
    {
        var center = (Taps - 1) / 2.0;
        double sum = 0;
        for (var n = 0; n < Taps; n++)
        {
            var x = n - center;
            var sinc = x == 0 ? 2 * cutoffRatio : Math.Sin(2 * Math.PI * cutoffRatio * x) / (Math.PI * x);
            var blackman = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * n / (Taps - 1));
            _kernel[n] = (float)(sinc * blackman);
            sum += _kernel[n];
        }
        for (var n = 0; n < Taps; n++) _kernel[n] /= (float)sum;
    }

    /// <summary>Filters interleaved stereo frames in place.</summary>
    public void Process(Span<float> stereo)
    {
        var frames = stereo.Length / 2;
        var total = Taps - 1 + frames;
        if (_work.Length < total * 2) _work = new float[total * 2];
        _history.CopyTo(_work, 0);
        stereo[..(frames * 2)].CopyTo(_work.AsSpan(_history.Length));

        for (var i = 0; i < frames; i++)
        {
            float left = 0, right = 0;
            var start = i * 2;
            for (var k = 0; k < Taps; k++)
            {
                var weight = _kernel[k];
                left += _work[start + k * 2] * weight;
                right += _work[start + k * 2 + 1] * weight;
            }
            stereo[i * 2] = left;
            stereo[i * 2 + 1] = right;
        }

        _work.AsSpan(frames * 2, _history.Length).CopyTo(_history);
    }
}
