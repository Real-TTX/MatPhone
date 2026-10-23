using System.Net;
using MatPhone.Core.Audio;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace MatPhone.Telephony;

/// <summary>
/// The audio of one leg: an RTP session (a SIP phone, a line) or a WebRTC connection (the browser), always G.711. It says which law the far end chose, passes
/// on what arrives (audio and DTMF events) and sends what the other side of the bridge gives it.
/// </summary>
public sealed class MediaPort : IDisposable
{
    public const int DtmfPayloadType = 101;

    private readonly object _gate = new();
    private Action<byte[], uint, int>? _audioSink;
    private Action<RTPEvent, RTPHeader>? _dtmfSink;
    private bool _closed;

    public MediaPort(RTPSession session)
    {
        Session = session;
        Session.AcceptRtpFromAny = true;
        Session.OnAudioFormatsNegotiated += formats =>
        {
            var format = formats.FirstOrDefault(f => f.Codec is AudioCodecsEnum.PCMA or AudioCodecsEnum.PCMU);
            if (!format.IsEmpty())
            {
                IsAlaw = format.Codec == AudioCodecsEnum.PCMA;
                AudioPayloadType = format.FormatID;
            }
        };
        Session.OnRtpPacketReceived += (_, kind, packet) =>
        {
            if (kind != SDPMediaTypesEnum.audio || packet.Header.PayloadType is not (0 or 8))
            {
                return;
            }

            LastReceived = DateTime.UtcNow;
            _audioSink?.Invoke(packet.Payload, packet.Header.Timestamp, packet.Header.PayloadType);
        };
        Session.OnRtpEvent += (_, rtpEvent, header) =>
        {
            // RFC 4733 sends the end of a key three times: one key press is the first end with a new timestamp.
            if (rtpEvent.EndOfEvent && header.Timestamp != _lastDigitTimestamp)
            {
                _lastDigitTimestamp = header.Timestamp;
                InjectDigit(DigitOf(rtpEvent.EventID));
            }

            _dtmfSink?.Invoke(rtpEvent, header);
        };

        // Plain RTP flows at once; WebRTC only after ICE and DTLS (what is sent before is lost).
        if (session is RTCPeerConnection pc && pc.connectionState != RTCPeerConnectionState.connected)
        {
            pc.onconnectionstatechange += state =>
            {
                if (state == RTCPeerConnectionState.connected)
                {
                    _ready.TrySetResult();
                }
            };
        }
        else
        {
            _ready.TrySetResult();
        }
    }

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits until audio can flow (a browser: connected), at most <paramref name="limit"/>.</summary>
    public async Task WaitReadyAsync(TimeSpan limit, CancellationToken ct)
    {
        try
        {
            await _ready.Task.WaitAsync(limit, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // play anyway: maybe the far end hears it
        }
        catch (OperationCanceledException)
        {
            // the call ended meanwhile
        }
    }

    public RTPSession Session { get; }

    /// <summary>A-law (PCMA) or µ-law (PCMU), as negotiated; A-law until the far end said otherwise.</summary>
    public bool IsAlaw { get; private set; } = true;

    public int AudioPayloadType { get; private set; } = 8;

    public DateTime LastReceived { get; private set; } = DateTime.UtcNow;

    /// <summary>The caller is on hold: nothing is sent to it (it would only hear a stream it asked not to get).</summary>
    public bool Muted { get; set; }

    private uint _lastDigitTimestamp = uint.MaxValue;

    /// <summary>A key was pressed (RFC 4733 events, or SIP INFO of the phones that send it so): the mailbox listens to it.</summary>
    public event Action<char>? DigitPressed;

    public void InjectDigit(char digit)
    {
        if (digit != default(char))
        {
            DigitPressed?.Invoke(digit);
        }
    }

    private static char DigitOf(byte eventId) => eventId switch
    {
        <= 9 => (char)('0' + eventId),
        10 => '*',
        11 => '#',
        _ => default(char),
    };

    /// <summary>What arrives goes to <paramref name="audio"/> (payload, timestamp, payload type 0 or 8) and <paramref name="dtmf"/>; null stops it.</summary>
    public void Connect(Action<byte[], uint, int>? audio, Action<RTPEvent, RTPHeader>? dtmf = null)
    {
        lock (_gate)
        {
            _audioSink = audio;
            _dtmfSink = dtmf;
        }
    }

    /// <summary>Sends a G.711 payload of the other side: converted when the laws differ.</summary>
    public void SendAudio(byte[] payload, uint timestamp, int payloadType)
    {
        if (_closed || Muted || Session.IsClosed)
        {
            return;
        }

        var fromAlaw = payloadType == 8;
        var data = payload;
        if (fromAlaw != IsAlaw)
        {
            data = (byte[])payload.Clone();
            G711.Transcode(data, fromAlaw);
        }

        try
        {
            Session.SendRtpRaw(SDPMediaTypesEnum.audio, data, timestamp, 0, AudioPayloadType);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The leg closes while the other side still sends: the last packets are lost, nothing else.
        }
    }

    /// <summary>Sends 20 ms of audio generated here (the greeting of a mailbox, a tone), in the law of the far end.</summary>
    public void SendFrame(ReadOnlySpan<short> pcm)
    {
        if (_closed || Session.IsClosed)
        {
            return;
        }

        var encoded = new byte[pcm.Length];
        G711.Encode(pcm, encoded, IsAlaw);
        try
        {
            Session.SendAudio((uint)pcm.Length, encoded);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
        }
    }

    public void SendDtmf(RTPEvent rtpEvent, RTPHeader header)
    {
        if (_closed || Session.IsClosed)
        {
            return;
        }

        try
        {
            Session.SendRtpRaw(SDPMediaTypesEnum.audio, rtpEvent.GetEventPayload(), header.Timestamp, header.MarkerBit, Session.AudioStream?.NegotiatedRtpEventPayloadID ?? DtmfPayloadType);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
        }
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Connect(null);
        try
        {
            Session.Close("bye");
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>The audio track every leg offers: A-law first (the German networks), µ-law, and DTMF events.</summary>
    public static MediaStreamTrack AudioTrack() => new(
    [
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
    ]);

    /// <summary>An RTP session for a SIP leg, on a port of the range of the settings.</summary>
    public static RTPSession CreateRtp(MediaPorts ports)
    {
        var session = new RTPSession(false, false, false, IPAddress.Any, 0, ports.Range);
        session.addTrack(AudioTrack());
        return session;
    }
}

/// <summary>The range of UDP ports for audio (shared by all legs) and the address that is written into the SDP.</summary>
public sealed class MediaPorts(int from, int to, IPAddress? publicAddress)
{
    public SIPSorcery.Sys.PortRange Range { get; } = new(from % 2 == 0 ? from : from + 1, to, shuffle: true);

    public int From { get; } = from;

    public int To { get; } = to;

    public IPAddress? PublicAddress { get; } = publicAddress;
}

/// <summary>Two legs that hear each other: what arrives on one is sent on the other, DTMF events too.</summary>
public sealed class MediaBridge : IDisposable
{
    private readonly MediaPort _a;
    private readonly MediaPort _b;

    public MediaBridge(MediaPort a, MediaPort b)
    {
        _a = a;
        _b = b;
        a.Connect(b.SendAudio, b.SendDtmf);
        b.Connect(a.SendAudio, a.SendDtmf);
    }

    public void Dispose()
    {
        _a.Connect(null);
        _b.Connect(null);
    }
}
