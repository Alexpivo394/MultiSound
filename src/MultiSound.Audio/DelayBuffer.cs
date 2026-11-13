namespace MultiSound.Audio;

/// <summary>
/// Ring buffer of interleaved stereo frames between the capture thread (writer) and a render thread (reader).
/// </summary>
/// <remarks>When full, a write drops the oldest frames. <see cref="InsertSilence"/> and <see cref="Skip"/>
/// change the delay at the read head and fade the joins so the change is not heard as a click.</remarks>
public sealed class DelayBuffer
{
    private readonly object _sync = new();
    private readonly float[] _data;
    private readonly int _capacity;
    private int _head; // first frame to read
    private int _count;

    public DelayBuffer(int capacityFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        _capacity = capacityFrames;
        _data = new float[capacityFrames * 2];
    }

    public int Count
    {
        get { lock (_sync) return _count; }
    }

    public void Write(ReadOnlySpan<float> stereo)
    {
        lock (_sync)
        {
            var frames = stereo.Length / 2;
            if (frames >= _capacity)
            {
                stereo.Slice((frames - _capacity) * 2, _capacity * 2).CopyTo(_data);
                _head = 0;
                _count = _capacity;
                return;
            }

            var overflow = _count + frames - _capacity;
            if (overflow > 0)
            {
                _head = (_head + overflow) % _capacity;
                _count -= overflow;
            }

            var tail = (_head + _count) % _capacity;
            var first = Math.Min(frames, _capacity - tail);
            stereo[..(first * 2)].CopyTo(_data.AsSpan(tail * 2));
            stereo[(first * 2)..(frames * 2)].CopyTo(_data);
            _count += frames;
        }
    }

    public int Read(Span<float> dstStereo)
    {
        lock (_sync)
        {
            var frames = Math.Min(dstStereo.Length / 2, _count);
            var first = Math.Min(frames, _capacity - _head);
            _data.AsSpan(_head * 2, first * 2).CopyTo(dstStereo);
            _data.AsSpan(0, (frames - first) * 2).CopyTo(dstStereo[(first * 2)..]);
            _head = (_head + frames) % _capacity;
            _count -= frames;
            return frames;
        }
    }

    /// <summary>Adds <paramref name="frames"/> of silence at the read head: fade out, silence, fade back in.</summary>
    public void InsertSilence(int frames, int fadeFrames)
    {
        if (frames <= 0) return;
        lock (_sync)
        {
            var content = Drain();
            var fade = Math.Min(fadeFrames, content.Length / 4);
            var result = new float[Math.Min(content.Length + frames * 2, _capacity * 2)];

            for (var i = 0; i < fade; i++)
                Scale(content, i, 1f - (i + 1f) / (fade + 1f));
            for (var i = 0; i < fade; i++)
                Scale(content, fade + i, (i + 1f) / (fade + 1f));

            var head = content.AsSpan(0, fade * 2);
            var rest = content.AsSpan(fade * 2);
            head.CopyTo(result);
            var restStart = (fade + frames) * 2;
            if (restStart < result.Length)
                rest[..Math.Min(rest.Length, result.Length - restStart)].CopyTo(result.AsSpan(restStart));
            Refill(result);
        }
    }

    /// <summary>Drops <paramref name="frames"/> at the read head, crossfading the join; empties the buffer when fewer remain.</summary>
    public void Skip(int frames, int fadeFrames)
    {
        if (frames <= 0) return;
        lock (_sync)
        {
            if (_count <= frames)
            {
                _head = 0;
                _count = 0;
                return;
            }

            var content = Drain();
            var total = content.Length / 2;
            var fade = Math.Min(fadeFrames, total - frames);
            for (var i = 0; i < fade; i++)
            {
                var t = (i + 1f) / (fade + 1f);
                var outIdx = (frames + i) * 2;
                content[outIdx] = content[i * 2] * (1 - t) + content[outIdx] * t;
                content[outIdx + 1] = content[i * 2 + 1] * (1 - t) + content[outIdx + 1] * t;
            }
            Refill(content.AsSpan(frames * 2));
        }
    }

    private static void Scale(float[] data, int frame, float gain)
    {
        data[frame * 2] *= gain;
        data[frame * 2 + 1] *= gain;
    }

    private float[] Drain()
    {
        var content = new float[_count * 2];
        Read(content);
        return content;
    }

    private void Refill(ReadOnlySpan<float> content)
    {
        _head = 0;
        _count = 0;
        Write(content);
    }
}
