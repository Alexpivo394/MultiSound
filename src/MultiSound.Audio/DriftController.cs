namespace MultiSound.Audio;

/// <summary>PI controller that keeps a copy's buffer fill at its target by nudging the playback rate.</summary>
/// <remarks>The fill is smoothed first, because capture and render move it in 10 ms steps.</remarks>
public sealed class DriftController
{
    public const double MaxCorrection = 0.005;

    private const double Kp = 3e-4;       // per ms of error
    private const double Ki = 4e-5;       // per ms·s of accumulated error
    private const double SmoothingSeconds = 1.0;

    private double _smoothed;
    private double _integral;
    private bool _initialized;

    /// <returns>Rate correction in [-<see cref="MaxCorrection"/>, <see cref="MaxCorrection"/>]; positive reads faster.</returns>
    public double Update(double fillMs, double targetMs, double dtSeconds)
    {
        if (!_initialized)
        {
            _smoothed = fillMs;
            _initialized = true;
        }
        else
        {
            _smoothed += (fillMs - _smoothed) * (1 - Math.Exp(-dtSeconds / SmoothingSeconds));
        }

        var error = _smoothed - targetMs;

        // Conditional integration: the integrator only moves while the output is not saturated (anti-windup).
        var integral = _integral + error * dtSeconds;
        var output = Kp * error + Ki * integral;
        if (Math.Abs(output) < MaxCorrection) _integral = integral;

        return Math.Clamp(Kp * error + Ki * _integral, -MaxCorrection, MaxCorrection);
    }

    public void Reset()
    {
        _initialized = false;
        _integral = 0;
    }
}
