using System.Buffers.Binary;

namespace MatPhone.Core.Audio;

/// <summary>
/// WAV files of the mailboxes: recordings are written as 8 kHz, 16 bit, mono PCM (every player can play it, and it is what G.711 decodes to); a greeting that is
/// uploaded may have another rate or two channels and is brought to that form (<see cref="ReadAs8kMono"/>).
/// </summary>
public static class Wav
{
    public static byte[] Write(ReadOnlySpan<short> samples, int sampleRate = G711.SampleRate)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];
        var span = bytes.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], samples[i]);
        }

        return bytes;
    }

    /// <summary>
    /// The samples of a PCM WAV file (8 or 16 bit, mono or stereo, any rate) at 8 kHz mono, or null when the file is not such a WAV. Stereo is mixed down, another
    /// rate is resampled linearly (a greeting is speech; that is good enough below the 3.4 kHz of the telephone).
    /// </summary>
    public static short[]? ReadAs8kMono(ReadOnlySpan<byte> file)
    {
        if (file.Length < 44 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..12].SequenceEqual("WAVE"u8))
        {
            return null;
        }

        int channels = 0, rate = 0, bits = 0;
        var offset = 12;
        ReadOnlySpan<byte> data = default;
        while (offset + 8 <= file.Length)
        {
            var id = file.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(file[(offset + 4)..]);
            var body = offset + 8;
            if (size < 0 || body + size > file.Length)
            {
                size = file.Length - body;
            }

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                var format = BinaryPrimitives.ReadInt16LittleEndian(file[body..]);
                // 1 = PCM, 0xFFFE = extensible (PCM inside, as browsers and editors write it)
                if (format != 1 && format != unchecked((short)0xFFFE))
                {
                    return null;
                }

                channels = BinaryPrimitives.ReadInt16LittleEndian(file[(body + 2)..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(file[(body + 4)..]);
                bits = BinaryPrimitives.ReadInt16LittleEndian(file[(body + 14)..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = file.Slice(body, size);
                break;
            }

            offset = body + size + (size & 1);
        }

        if (channels is < 1 or > 2 || rate is < 4000 or > 192000 || bits is not (8 or 16) || data.IsEmpty)
        {
            return null;
        }

        var frameBytes = channels * bits / 8;
        var frames = data.Length / frameBytes;
        var mono = new short[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var at = f * frameBytes + c * bits / 8;
                sum += bits == 16 ? BinaryPrimitives.ReadInt16LittleEndian(data[at..]) : (data[at] - 128) << 8;
            }

            mono[f] = (short)(sum / channels);
        }

        return rate == G711.SampleRate ? mono : Resample(mono, rate, G711.SampleRate);
    }

    public static short[] Resample(short[] input, int fromRate, int toRate)
    {
        if (input.Length == 0 || fromRate == toRate)
        {
            return input;
        }

        var length = (int)((long)input.Length * toRate / fromRate);
        var output = new short[length];
        var step = (double)fromRate / toRate;
        for (var i = 0; i < length; i++)
        {
            var pos = i * step;
            var index = (int)pos;
            var next = Math.Min(index + 1, input.Length - 1);
            var frac = pos - index;
            output[i] = (short)Math.Round(input[index] * (1 - frac) + input[next] * frac);
        }

        return output;
    }

    /// <summary>A sine tone at 8 kHz (the beep after a greeting), with 10 ms of fade at both ends so that it does not click.</summary>
    public static short[] Tone(int frequency, int milliseconds, double level = 0.3)
    {
        var count = G711.SampleRate * milliseconds / 1000;
        var samples = new short[count];
        var fade = Math.Min(count / 2, G711.SampleRate / 100);
        for (var i = 0; i < count; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)Math.Max(1, fade));
            samples[i] = (short)(Math.Sin(2 * Math.PI * frequency * i / G711.SampleRate) * short.MaxValue * level * envelope);
        }

        return samples;
    }

    public static short[] Silence(int milliseconds) => new short[G711.SampleRate * milliseconds / 1000];

    public static int DurationSeconds(int samples) => (int)Math.Round(samples / (double)G711.SampleRate);
}
