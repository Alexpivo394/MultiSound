namespace MultiSound.Audio;

/// <summary>Converts interleaved audio between a device's channel layout and stereo.</summary>
/// <remarks>Layouts with three or more channels follow the WAVE order: FL, FR, FC, LFE, ….</remarks>
public static class ChannelMapper
{
    private const float MixGain = 0.70710677f;

    /// <summary>Scale applied to a surround downmix so typical content stays below full scale; peaks are clamped.</summary>
    public const float SurroundHeadroom = 1f / (1f + MixGain);

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
                var layout = Layout(srcChannels);
                for (var i = 0; i < frames; i++)
                {
                    var frame = src.Slice(i * srcChannels, srcChannels);
                    float left = frame[0], right = frame[1];
                    if (layout.Center >= 0)
                    {
                        left += frame[layout.Center] * MixGain;
                        right += frame[layout.Center] * MixGain;
                    }
                    foreach (var channel in layout.Left) left += frame[channel] * MixGain;
                    foreach (var channel in layout.Right) right += frame[channel] * MixGain;
                    dstStereo[2 * i] = Math.Clamp(left * SurroundHeadroom, -1f, 1f);
                    dstStereo[2 * i + 1] = Math.Clamp(right * SurroundHeadroom, -1f, 1f);
                }
                break;
        }
    }

    /// <summary>Positions of the center and the extra left/right channels in the usual WAVE layouts; LFE is dropped.</summary>
    private static (int Center, int[] Left, int[] Right) Layout(int channels) => channels switch
    {
        3 => (2, [], []),                 // FL FR FC
        4 => (-1, [2], [3]),              // FL FR BL BR
        5 => (2, [3], [4]),               // FL FR FC BL BR
        6 => (2, [4], [5]),               // FL FR FC LFE BL BR
        8 => (2, [4, 6], [5, 7]),         // FL FR FC LFE BL BR SL SR
        _ => (2, [], []),
    };

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
