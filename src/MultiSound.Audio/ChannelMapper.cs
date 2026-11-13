namespace MultiSound.Audio;

/// <summary>Converts interleaved audio between a device's channel layout and stereo.</summary>
/// <remarks>Layouts with three or more channels follow the WAVE order: FL, FR, FC, LFE, ….</remarks>
public static class ChannelMapper
{
    private const float CenterGain = 0.70710677f;

    public static void ToStereo(ReadOnlySpan<float> src, int srcChannels, Span<float> dstStereo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(srcChannels, 1);
        var frames = src.Length / srcChannels;

        switch (srcChannels)
        {
            case 1:
                for (var i = 0; i < frames; i++)
                {
                    dstStereo[2 * i] = src[i];
                    dstStereo[2 * i + 1] = src[i];
                }
                break;
            case 2:
                src[..(frames * 2)].CopyTo(dstStereo);
                break;
            default:
                for (var i = 0; i < frames; i++)
                {
                    var frame = src.Slice(i * srcChannels, srcChannels);
                    var center = frame[2] * CenterGain;
                    dstStereo[2 * i] = frame[0] + center;
                    dstStereo[2 * i + 1] = frame[1] + center;
                }
                break;
        }
    }

    public static void FromStereo(ReadOnlySpan<float> srcStereo, Span<float> dst, int dstChannels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dstChannels, 1);
        var frames = srcStereo.Length / 2;

        switch (dstChannels)
        {
            case 1:
                for (var i = 0; i < frames; i++)
                    dst[i] = (srcStereo[2 * i] + srcStereo[2 * i + 1]) * 0.5f;
                break;
            case 2:
                srcStereo[..(frames * 2)].CopyTo(dst);
                break;
            default:
                for (var i = 0; i < frames; i++)
                {
                    var frame = dst.Slice(i * dstChannels, dstChannels);
                    frame.Clear();
                    frame[0] = srcStereo[2 * i];
                    frame[1] = srcStereo[2 * i + 1];
                }
                break;
        }
    }
}
