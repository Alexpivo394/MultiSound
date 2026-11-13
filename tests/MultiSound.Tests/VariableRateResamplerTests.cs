using MultiSound.Audio;

namespace MultiSound.Tests;

public class VariableRateResamplerTests
{
    private static DelayBuffer Filled(float[] stereo)
    {
        var buffer = new DelayBuffer(stereo.Length / 2 + 16);
        buffer.Write(stereo);
        return buffer;
    }

    [Test]
    public async Task UnitStep_PassesThrough()
    {
        var input = Signals.StereoSine(1000, 48000, 1000);
        var source = Filled(input);
        var output = new float[900 * 2];
        new VariableRateResampler().Render(source, output, 1.0);

        var matched = -1;
        for (var delay = 0; delay <= 3 && matched < 0; delay++)
        {
            var ok = true;
            for (var i = 10; i < 900 && ok; i++)
                ok = Math.Abs(output[2 * i] - input[2 * (i - delay)]) < 1e-5;
            if (ok) matched = delay;
        }
        await Assert.That(matched).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Step_ConsumesProportionally()
    {
        var source = Filled(Signals.StereoSine(1000, 48000, 50_000));
        var resampler = new VariableRateResampler();
        var output = new float[441 * 2];
        for (var i = 0; i < 100; i++)
            resampler.Render(source, output, 48000.0 / 44100);
        var consumed = 50_000 - source.Count - resampler.PendingFrames;
        await Assert.That(consumed).IsBetween(48_000 - 4, 48_000 + 4);
    }

    [Test]
    public async Task SineFrequency_ScalesWithStep()
    {
        var source = Filled(Signals.StereoSine(1000, 48000, 50_000));
        var output = new float[48_000 * 2];
        var produced = new VariableRateResampler().Render(source, output, 1.005);
        await Assert.That(produced).IsEqualTo(48_000);
        await Assert.That(Signals.Frequency(output, 2, 48000)).IsBetween(1003.0, 1007.0);
    }

    [Test]
    public async Task StepChange_Continuous()
    {
        var input = Signals.StereoSine(1000, 48000, 20_000);
        var source = Filled(input);
        var resampler = new VariableRateResampler();
        var output = new float[100 * 2];
        var all = new List<float>();
        for (var i = 0; i < 150; i++)
        {
            var produced = resampler.Render(source, output, i % 2 == 0 ? 0.995 : 1.005);
            all.AddRange(output[..(produced * 2)]);
        }
        var inputDiff = Signals.MaxAdjacentDiff(input, 2);
        await Assert.That(Signals.MaxAdjacentDiff(all.ToArray(), 2)).IsLessThanOrEqualTo(inputDiff * 1.1f);
    }

    [Test]
    public async Task Underflow_ReturnsFewerFrames()
    {
        var source = Filled(Signals.StereoSine(1000, 48000, 100));
        var output = new float[500 * 2];
        var produced = new VariableRateResampler().Render(source, output, 1.0);
        await Assert.That(produced).IsBetween(95, 101);
    }
}
