using MultiSound.Audio;

namespace MultiSound.Tests;

public class ChannelMapperTests
{
    [Test]
    public async Task Mono_ToStereo_Duplicates()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo([0.5f], 1, dst);
        await Assert.That(dst).IsEquivalentTo(new[] { 0.5f, 0.5f });
    }

    [Test]
    public async Task Stereo_RoundTrip_Identity()
    {
        float[] src = [0.1f, -0.2f, 0.3f, 0.4f];
        var stereo = new float[4];
        var back = new float[4];
        ChannelMapper.ToStereo(src, 2, stereo);
        ChannelMapper.FromStereo(stereo, back, 2);
        await Assert.That(back).IsEquivalentTo(src);
    }

    [Test]
    public async Task Surround51_ToStereo_AddsCenter()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo([0.2f, 0.4f, 0.5f, 1f, 0f, 0f], 6, dst);
        await Assert.That(dst[0]).IsEqualTo((0.2f + 0.7071f * 0.5f) * ChannelMapper.SurroundHeadroom).Within(1e-4f);
        await Assert.That(dst[1]).IsEqualTo((0.4f + 0.7071f * 0.5f) * ChannelMapper.SurroundHeadroom).Within(1e-4f);
    }

    [Test]
    public async Task Surround51_ToStereo_KeepsSurrounds()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo([0f, 0f, 0f, 0f, 0.4f, 0f], 6, dst);
        await Assert.That(dst[0]).IsEqualTo(0.7071f * 0.4f * ChannelMapper.SurroundHeadroom).Within(1e-4f);
        await Assert.That(dst[1]).IsEqualTo(0f);
    }

    [Test]
    public async Task Surround71_ToStereo_KeepsSideAndBack()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo([0f, 0f, 0f, 0f, 0f, 0.3f, 0f, 0.3f], 8, dst);
        await Assert.That(dst[0]).IsEqualTo(0f);
        await Assert.That(dst[1]).IsEqualTo(2 * 0.7071f * 0.3f * ChannelMapper.SurroundHeadroom).Within(1e-4f);
    }

    [Test]
    public async Task Quad_ToStereo_RearIsNotCenter()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo([0f, 0f, 0.4f, 0f], 4, dst);
        await Assert.That(dst[0]).IsEqualTo(0.7071f * 0.4f * ChannelMapper.SurroundHeadroom).Within(1e-4f);
        await Assert.That(dst[1]).IsEqualTo(0f);
    }

    [Test]
    public async Task Surround_FullScale_DoesNotClip()
    {
        var dst = new float[2];
        ChannelMapper.ToStereo(Enumerable.Repeat(1f, 8).ToArray(), 8, dst);
        await Assert.That(dst.All(v => v <= 1f)).IsTrue();
    }

    [Test]
    public async Task Stereo_ToMono_Averages()
    {
        var dst = new float[1];
        ChannelMapper.FromStereo([0.2f, 0.6f], dst, 1);
        await Assert.That(dst[0]).IsEqualTo(0.4f).Within(1e-6f);
    }

    [Test]
    public async Task Stereo_To51_FillsFrontZerosRest()
    {
        var dst = Enumerable.Repeat(9f, 6).ToArray();
        ChannelMapper.FromStereo([0.2f, 0.6f], dst, 6);
        await Assert.That(dst).IsEquivalentTo(new[] { 0.2f, 0.6f, 0f, 0f, 0f, 0f });
    }
}
