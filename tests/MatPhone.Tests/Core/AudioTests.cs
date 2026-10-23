using MatPhone.Core.Audio;

namespace MatPhone.Tests.Core;

/// <summary>G.711 as the reference code of ITU-T G.191 does it, and the WAV files of the mailboxes.</summary>
public sealed class AudioTests
{
    [Fact]
    public void Silence_has_the_well_known_codes()
    {
        Assert.Equal(0xD5, G711.EncodeAlaw(0));
        Assert.Equal(0xFF, G711.EncodeUlaw(0));
        Assert.Equal(0, G711.DecodeUlaw(0xFF));
        Assert.Equal(8, G711.DecodeAlaw(0xD5));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_sample_comes_back_close_to_itself(bool alaw)
    {
        for (var x = short.MinValue + 1; x < short.MaxValue; x += 97)
        {
            var sample = (short)x;
            var back = alaw ? G711.DecodeAlaw(G711.EncodeAlaw(sample)) : G711.DecodeUlaw(G711.EncodeUlaw(sample));
            var allowed = Math.Max(64, Math.Abs(x) / 16);
            Assert.True(Math.Abs(back - x) <= allowed, $"{x} came back as {back} ({(alaw ? "A-law" : "µ-law")})");
        }
    }

    [Fact]
    public void Every_code_survives_encoding_its_own_value()
    {
        for (var code = 0; code < 256; code++)
        {
            Assert.Equal(G711.DecodeAlaw((byte)code), G711.DecodeAlaw(G711.EncodeAlaw(G711.DecodeAlaw((byte)code))));
            Assert.Equal(G711.DecodeUlaw((byte)code), G711.DecodeUlaw(G711.EncodeUlaw(G711.DecodeUlaw((byte)code))));
        }
    }

    [Fact]
    public void Transcoding_keeps_the_sound()
    {
        var tone = Wav.Tone(440, 100);
        var alaw = new byte[tone.Length];
        G711.Encode(tone, alaw, alaw: true);

        var payload = alaw.ToArray();
        G711.Transcode(payload, fromAlaw: true);
        var viaUlaw = new short[tone.Length];
        G711.Decode(payload, viaUlaw, alaw: false);

        for (var i = 0; i < tone.Length; i++)
        {
            Assert.True(Math.Abs(viaUlaw[i] - tone[i]) <= Math.Max(128, Math.Abs((int)tone[i]) / 8), $"sample {i}: {tone[i]} became {viaUlaw[i]}");
        }
    }

    [Fact]
    public void A_wav_is_read_back_as_it_was_written()
    {
        var tone = Wav.Tone(1000, 250);
        var file = Wav.Write(tone);
        Assert.Equal(44 + tone.Length * 2, file.Length);
        Assert.Equal(tone, Wav.ReadAs8kMono(file));
        Assert.Equal(0, Wav.DurationSeconds(Wav.ReadAs8kMono(file)!.Length));
        Assert.Equal(2, Wav.DurationSeconds(Wav.Silence(2000).Length));
    }

    [Fact]
    public void Another_rate_is_brought_to_8_kHz()
    {
        var file = Wav.Write(new short[48000], 48000);
        Assert.Equal(8000, Wav.ReadAs8kMono(file)!.Length);
    }

    [Fact]
    public void What_is_not_a_wav_is_refused()
    {
        Assert.Null(Wav.ReadAs8kMono("das ist keine Aufnahme, sondern nur ein Text ohne Kopf"u8));
        Assert.Null(Wav.ReadAs8kMono([]));
    }

    [Fact]
    public void A_tone_fades_in_and_out()
    {
        var tone = Wav.Tone(440, 200);
        Assert.Equal(1600, tone.Length);
        Assert.Equal(0, tone[0]);
        Assert.True(Math.Abs((int)tone[^1]) < 100);
        Assert.True(tone.Max() > 9000);
    }
}
