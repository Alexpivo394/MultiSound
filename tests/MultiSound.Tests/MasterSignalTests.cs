using MultiSound.Audio;
using NAudio.Wave;

namespace MultiSound.Tests;

public class MasterSignalTests
{
    private static float[] Render(MasterSignal signal, double seconds)
    {
        var format = signal.WaveFormat;
        var all = new List<float>();
        var block = new float[format.SampleRate / 100 * format.Channels];
        for (var i = 0; i < (int)Math.Round(seconds * 100); i++)
        {
            signal.Read(block, 0, block.Length);
            all.AddRange(block);
        }
        return all.ToArray();
    }

    /// <summary>Start frames of each click: a non-zero frame after at least 100 ms of silence.</summary>
    private static List<int> ClickStarts(float[] stereo)
    {
        var starts = new List<int>();
        var lastNonZero = int.MinValue / 2;
        for (var frame = 0; frame < stereo.Length / 2; frame++)
        {
            if (stereo[frame * 2] == 0f) continue;
            if (frame - lastNonZero > 4800) starts.Add(frame);
            lastNonZero = frame;
        }
        return starts;
    }

    [Test]
    public async Task Disabled_Silence()
    {
        var signal = new MasterSignal(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        await Assert.That(Render(signal, 2).All(v => v == 0f)).IsTrue();
    }

    [Test]
    public async Task Enabled_ClickEverySecond()
    {
        var signal = new MasterSignal(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ClicksEnabled = true };
        var starts = ClickStarts(Render(signal, 3.5));

        await Assert.That(starts.Count).IsEqualTo(4);
        await Assert.That(starts[0]).IsLessThan(480);
        for (var i = 1; i < starts.Count; i++)
            await Assert.That(starts[i] - starts[i - 1]).IsBetween(47_999, 48_001);
    }

    [Test]
    public async Task Click_PeakAndLength()
    {
        var signal = new MasterSignal(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ClicksEnabled = true };
        var stereo = Render(signal, 0.5);

        var nonZero = Enumerable.Range(0, stereo.Length / 2).Where(f => stereo[f * 2] != 0f).ToList();
        await Assert.That(stereo.Max(Math.Abs)).IsBetween(0.49f, 0.51f);
        await Assert.That(nonZero[^1] - nonZero[0] + 1).IsBetween(238, 242);
        await Assert.That(Enumerable.Range(0, stereo.Length / 2).All(f => stereo[f * 2] == stereo[f * 2 + 1])).IsTrue();
    }
}
