namespace MatPhone.Core.Audio;

/// <summary>
/// G.711, the codec of the telephone network: 8 kHz, one byte per sample, A-law (PCMA, Europe) or µ-law (PCMU). Every leg of a call speaks one of them, the
/// browser too; when two legs chose different ones the bridge converts byte by byte (<see cref="AlawToUlaw"/>, <see cref="UlawToAlaw"/>).
/// </summary>
public static class G711
{
    public const int SampleRate = 8000;

    /// <summary>20 ms of audio: the size of one RTP packet.</summary>
    public const int SamplesPerFrame = 160;

    private static readonly short[] AlawTable = BuildAlawTable();
    private static readonly short[] UlawTable = BuildUlawTable();
    private static readonly byte[] AToU = new byte[256];
    private static readonly byte[] UToA = new byte[256];

    static G711()
    {
        for (var i = 0; i < 256; i++)
        {
            AToU[i] = EncodeUlaw(AlawTable[i]);
            UToA[i] = EncodeAlaw(UlawTable[i]);
        }
    }

    public static short DecodeAlaw(byte value) => AlawTable[value];

    public static short DecodeUlaw(byte value) => UlawTable[value];

    public static byte AlawToUlaw(byte value) => AToU[value];

    public static byte UlawToAlaw(byte value) => UToA[value];

    private static readonly int[] AlawSegmentEnds = [0x1F, 0x3F, 0x7F, 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF];
    private static readonly int[] UlawSegmentEnds = [0x3F, 0x7F, 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF, 0x1FFF];

    /// <summary>16 bit linear to A-law (the reference algorithm of the ITU, as in Sun's g711.c).</summary>
    public static byte EncodeAlaw(short pcm)
    {
        var value = pcm >> 3;
        int mask;
        if (value >= 0)
        {
            mask = 0xD5;
        }
        else
        {
            mask = 0x55;
            value = -value - 1;
        }

        var segment = Segment(value, AlawSegmentEnds);
        if (segment >= 8)
        {
            return (byte)(0x7F ^ mask);
        }

        var encoded = segment << 4;
        encoded |= segment < 2 ? (value >> 1) & 0x0F : (value >> segment) & 0x0F;
        return (byte)(encoded ^ mask);
    }

    /// <summary>16 bit linear to µ-law.</summary>
    public static byte EncodeUlaw(short pcm)
    {
        var value = pcm >> 2;
        int mask;
        if (value < 0)
        {
            value = -value;
            mask = 0x7F;
        }
        else
        {
            mask = 0xFF;
        }

        if (value > 8159)
        {
            value = 8159;
        }

        value += 0x84 >> 2;
        var segment = Segment(value, UlawSegmentEnds);
        if (segment >= 8)
        {
            return (byte)(0x7F ^ mask);
        }

        return (byte)(((segment << 4) | ((value >> (segment + 1)) & 0x0F)) ^ mask);
    }

    private static int Segment(int value, int[] ends)
    {
        for (var i = 0; i < ends.Length; i++)
        {
            if (value <= ends[i])
            {
                return i;
            }
        }

        return ends.Length;
    }

    public static void Encode(ReadOnlySpan<short> pcm, Span<byte> target, bool alaw)
    {
        for (var i = 0; i < pcm.Length; i++)
        {
            target[i] = alaw ? EncodeAlaw(pcm[i]) : EncodeUlaw(pcm[i]);
        }
    }

    public static void Decode(ReadOnlySpan<byte> encoded, Span<short> target, bool alaw)
    {
        for (var i = 0; i < encoded.Length; i++)
        {
            target[i] = alaw ? AlawTable[encoded[i]] : UlawTable[encoded[i]];
        }
    }

    /// <summary>Converts a payload from one law to the other in place.</summary>
    public static void Transcode(Span<byte> payload, bool fromAlaw)
    {
        var table = fromAlaw ? AToU : UToA;
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = table[payload[i]];
        }
    }

    private static short[] BuildAlawTable()
    {
        var table = new short[256];
        for (var i = 0; i < 256; i++)
        {
            var a = i ^ 0x55;
            var t = (a & 0x0F) << 4;
            var seg = (a & 0x70) >> 4;
            switch (seg)
            {
                case 0:
                    t += 8;
                    break;
                case 1:
                    t += 0x108;
                    break;
                default:
                    t += 0x108;
                    t <<= seg - 1;
                    break;
            }

            table[i] = (short)((a & 0x80) != 0 ? t : -t);
        }

        return table;
    }

    private static short[] BuildUlawTable()
    {
        var table = new short[256];
        for (var i = 0; i < 256; i++)
        {
            var u = ~i & 0xFF;
            var t = ((u & 0x0F) << 3) + 0x84;
            t <<= (u & 0x70) >> 4;
            table[i] = (short)((u & 0x80) != 0 ? 0x84 - t : t - 0x84);
        }

        return table;
    }
}
