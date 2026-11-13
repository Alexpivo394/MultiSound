using MultiSound.Audio;
using NAudio.Wave;

namespace MultiSound.Tests;

public class CopyStreamTests
{
    private static (CopyStream Stream, CopyStreamSimulator Sim) Setup(int inRate, int outRate, int outChannels, double offsetMs, double drift = 0)
    {
        var clock = new VirtualClock();
        var stream = new CopyStream(inRate, WaveFormat.CreateIeeeFloatWaveFormat(outRate, outChannels), offsetMs, () => clock.Seconds);
        return (stream, new CopyStreamSimulator(stream, inRate, clock, drift));
    }

    [Test]
    [Arguments(0.0002)]
    [Arguments(-0.0002)]
    public async Task ClockDrift_3Hours_HoldsTarget(double drift)
    {
        var (stream, sim) = Setup(48000, 48000, 2, offsetMs: 100);
        
        sim.Run(60);

        var means = sim.WindowMeans(3 * 3600);

        await Assert.That(means.Min()).IsGreaterThanOrEqualTo(115);
        await Assert.That(means.Max()).IsLessThanOrEqualTo(125);
    }

    [Test]
    public async Task DifferentRates_PitchPreserved()
    {
        var (stream, sim) = Setup(48000, 44100, 2, offsetMs: 50);
        sim.Source = CopyStreamSimulator.Sine(1000, 48000);
        sim.Run(30);

        var lastSecond = new List<float>();
        var means = sim.WindowMeans(1, block => lastSecond.AddRange(block));

        await Assert.That(Signals.Frequency(lastSecond.ToArray(), 2, 44100)).IsBetween(998.0, 1002.0);
        await Assert.That(means.Single()).IsBetween(65.0, 75.0);
    }

    [Test]
    public async Task OffsetChange_Small_ConvergesSmoothly()
    {
        var (stream, sim) = Setup(48000, 48000, 2, offsetMs: 100);
        sim.Source = CopyStreamSimulator.Sine(440, 48000);
        sim.Run(30);

        stream.OffsetMs = 130;
        var longestSilence = 0;
        var run = 0;
        var means = sim.WindowMeans(15, block =>
        {
            for (var i = 0; i < block.Length; i += 2)
            {
                run = Math.Abs(block[i]) < 1e-6 ? run + 1 : 0;
                longestSilence = Math.Max(longestSilence, run);
            }
        });

        await Assert.That(means[^1]).IsBetween(145.0, 155.0);
        await Assert.That(longestSilence).IsLessThan(10);
    }

    [Test]
    public async Task OffsetJump_NoClicks()
    {
        var (stream, sim) = Setup(48000, 48000, 2, offsetMs: 0);
        sim.Source = CopyStreamSimulator.Sine(440, 48000);
        sim.Run(5);

        var output = new List<float>();
        foreach (var offset in new[] { 300.0, 50, 500, 0 })
        {
            stream.OffsetMs = offset;
            sim.Run(0.5, block => output.AddRange(block));
        }

        await Assert.That(Signals.MaxAdjacentDiff(output.ToArray(), 2)).IsLessThanOrEqualTo(0.1f);
    }

    [Test]
    public async Task Underflow_RecoversToTarget()
    {
        var (stream, sim) = Setup(48000, 48000, 2, offsetMs: 100);
        sim.Source = CopyStreamSimulator.Sine(440, 48000);
        sim.Run(10);

        sim.WriteEnabled = t => t < 10 || t >= 12;
        sim.Run(2);
        var means = sim.WindowMeans(1);

        await Assert.That(means.Single()).IsBetween(115.0, 125.0);
    }

    [Test]
    public async Task Volume_Scales()
    {
        var (stream, sim) = Setup(48000, 48000, 2, offsetMs: 0);
        stream.Volume = 0.5f;
        sim.Source = CopyStreamSimulator.Sine(440, 48000);
        sim.Run(2);

        var peak = 0f;
        sim.Run(1, block => peak = Math.Max(peak, block.Max(Math.Abs)));
        await Assert.That(peak).IsBetween(0.49f, 0.51f);
    }

    [Test]
    public async Task MonoOutput_Averages()
    {
        var (stream, sim) = Setup(48000, 48000, 1, offsetMs: 0);
        sim.Source = (_, count) => Enumerable.Range(0, count * 2).Select(i => i % 2 == 0 ? 0.2f : 0.6f).ToArray();
        sim.Run(2);

        float[] last = [];
        sim.Run(0.1, block => last = (float[])block.Clone());
        await Assert.That(last.All(v => Math.Abs(v - 0.4f) < 1e-4)).IsTrue();
    }
}
