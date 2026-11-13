using MultiSound.Audio;

namespace MultiSound.Tests;

public class DelayBufferTests
{
    private static float[] ReadAll(DelayBuffer buffer)
    {
        var dst = new float[buffer.Count * 2];
        var read = buffer.Read(dst);
        return dst[..(read * 2)];
    }

    [Test]
    public async Task WriteRead_Fifo()
    {
        var buffer = new DelayBuffer(16);
        buffer.Write(Signals.StereoRamp(10));
        await Assert.That(ReadAll(buffer)).IsEquivalentTo(Signals.StereoRamp(10));
    }

    [Test]
    public async Task Overflow_DropsOldest()
    {
        var buffer = new DelayBuffer(4);
        buffer.Write(Signals.StereoRamp(6));
        await Assert.That(buffer.Count).IsEqualTo(4);
        await Assert.That(ReadAll(buffer)).IsEquivalentTo(Signals.StereoRamp(6)[4..]);
    }

    [Test]
    public async Task InsertSilence_IncreasesCount()
    {
        var buffer = new DelayBuffer(10_000);
        buffer.Write(Signals.StereoSine(440, 48000, 2000));
        buffer.InsertSilence(1500, 240);
        await Assert.That(buffer.Count).IsEqualTo(3500);
    }

    [Test]
    public async Task Skip_DecreasesCount()
    {
        var buffer = new DelayBuffer(10_000);
        buffer.Write(Signals.StereoSine(440, 48000, 2000));
        buffer.Skip(700, 240);
        await Assert.That(buffer.Count).IsEqualTo(1300);
    }

    [Test]
    public async Task Skip_MoreThanAvailable_Empties()
    {
        var buffer = new DelayBuffer(10_000);
        buffer.Write(Signals.StereoSine(440, 48000, 500));
        buffer.Skip(700, 240);
        await Assert.That(buffer.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Skip_Crossfade_NoJump()
    {
        var buffer = new DelayBuffer(48_000);
        buffer.Write(Signals.StereoSine(440, 48000, 10_000));
        buffer.Skip(1000, 240);
        await Assert.That(Signals.MaxAdjacentDiff(ReadAll(buffer), 2)).IsLessThanOrEqualTo(0.1f);
    }

    [Test]
    public async Task InsertSilence_Fade_NoJump()
    {
        var buffer = new DelayBuffer(48_000);
        buffer.Write(Signals.StereoSine(440, 48000, 10_000));
        buffer.InsertSilence(4800, 240);
        await Assert.That(Signals.MaxAdjacentDiff(ReadAll(buffer), 2)).IsLessThanOrEqualTo(0.1f);
    }
}
