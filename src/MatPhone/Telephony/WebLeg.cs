using System.Net;
using System.Text.Json.Nodes;
using SIPSorcery.Net;

namespace MatPhone.Telephony;

/// <summary>
/// A browser as a party of a call (the web client): signalling over the WebSocket of its page, audio over WebRTC (G.711 inside DTLS-SRTP). The engine is always
/// the side that offers; the browser answers. For an incoming call the browser rings until the person accepts; for a call the person dials, the audio is set up
/// at once and the browser plays its own ring tone until the far end answers.
/// </summary>
public sealed class WebLeg : Leg
{
    private readonly WebClientConnection _connection;
    private readonly MediaPorts _ports;
    private readonly TelephonyLogWriter _log;
    private TaskCompletionSource<string?>? _answer;
    private RTCPeerConnection? _pc;

    public WebLeg(WebClientConnection connection, MediaPorts ports, TelephonyLogWriter log)
    {
        _connection = connection;
        _ports = ports;
        _log = log;
    }

    public WebClientConnection Connection => _connection;

    /// <summary>Whether the person dialled from this browser (an outgoing call) or it rings here.</summary>
    public bool IsOutgoing { get; private set; }

    public CallerId? Peer { get; private set; }

    private async Task<string> OfferAsync()
    {
        var config = new RTCConfiguration { X_UseRtpFeedbackProfile = false };
        var pc = new RTCPeerConnection(config, 0, _ports.Range);
        _pc = pc;
        pc.addTrack(MediaPort.AudioTrack());
        if (_ports.PublicAddress is { } address)
        {
            pc.addLocalIceCandidate(new RTCIceCandidate(RTCIceProtocol.udp, address, (ushort)pc.GetRtpChannel().RTPPort, RTCIceCandidateType.host));
        }

        pc.onconnectionstatechange += state =>
        {
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed && !IsEnded)
            {
                _log.Info(Data.Entities.LogCategory.Call, $"The browser connection of {Label} was lost ({state})", userId: UserId);
                RaiseEnded("webrtc " + state);
            }
        };
        Media = new MediaPort(pc);
        var offer = pc.createOffer();
        await pc.setLocalDescription(offer).ConfigureAwait(false);
        return offer.sdp;
    }

    /// <summary>The browser accepted an incoming call (or set up the audio of an outgoing one) with its answer.</summary>
    public void OnAnswer(string? sdp) => _answer?.TrySetResult(sdp);

    public void OnCandidate(string json)
    {
        if (_pc is not null && RTCIceCandidateInit.TryParse(json, out var init))
        {
            _pc.addIceCandidate(init);
        }
    }

    public void OnRemoteHangup() => RaiseEnded("browser");

    private bool ApplyAnswer(string? sdp)
    {
        if (_pc is null || string.IsNullOrWhiteSpace(sdp))
        {
            return false;
        }

        return _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp }) == SetDescriptionResultEnum.OK;
    }

    // ---- as callee: it rings in the browser ----

    public override async Task<bool> RingAsync(CallerId caller, CancellationToken ct)
    {
        Peer = caller;
        _answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sdp = await OfferAsync().ConfigureAwait(false);
        RaiseRinging();
        _connection.Send(new JsonObject
        {
            ["t"] = "incoming",
            ["leg"] = Id,
            ["number"] = caller.Number,
            ["name"] = caller.Name,
            ["called"] = caller.CalledNumber,
            ["sdp"] = sdp,
        });
        string? answer;
        using (ct.Register(() => _answer.TrySetResult(null)))
        {
            answer = await _answer.Task.ConfigureAwait(false);
        }

        if (answer is null || !ApplyAnswer(answer))
        {
            if (!IsEnded)
            {
                _connection.Send(new JsonObject { ["t"] = "ended", ["leg"] = Id, ["reason"] = ct.IsCancellationRequested ? "cancelled" : "rejected" });
            }

            RaiseEnded(ct.IsCancellationRequested ? "cancelled" : "rejected");
            return false;
        }

        State = LegState.Answered;
        _connection.Send(new JsonObject { ["t"] = "state", ["leg"] = Id, ["state"] = "connected" });
        return true;
    }

    // ---- as caller: the person dialled ----

    /// <summary>Sets up the audio of a call the person dialled: offer, answer of the browser. False when the browser did not answer within ten seconds.</summary>
    public async Task<bool> StartOutgoingAsync(string number)
    {
        IsOutgoing = true;
        Peer = new CallerId(number, null);
        _answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sdp = await OfferAsync().ConfigureAwait(false);
        _connection.Send(new JsonObject { ["t"] = "outgoing", ["leg"] = Id, ["number"] = number, ["sdp"] = sdp });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (timeout.Token.Register(() => _answer.TrySetResult(null)))
        {
            return ApplyAnswer(await _answer.Task.ConfigureAwait(false));
        }
    }

    public override void Progress()
    {
        if (!IsEnded)
        {
            _connection.Send(new JsonObject { ["t"] = "state", ["leg"] = Id, ["state"] = "ringing" });
        }
    }

    public override Task<bool> AnswerAsync()
    {
        if (IsEnded)
        {
            return Task.FromResult(false);
        }

        if (State != LegState.Answered)
        {
            State = LegState.Answered;
            _connection.Send(new JsonObject { ["t"] = "state", ["leg"] = Id, ["state"] = "connected" });
        }

        return Task.FromResult(true);
    }

    public override void Reject(int status, string reason)
    {
        if (!IsEnded)
        {
            _connection.Send(new JsonObject { ["t"] = "ended", ["leg"] = Id, ["reason"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture), ["text"] = reason });
        }

        RaiseEnded(reason);
    }

    public override void Hangup()
    {
        if (IsEnded)
        {
            return;
        }

        _answer?.TrySetResult(null);
        _connection.Send(new JsonObject { ["t"] = "ended", ["leg"] = Id, ["reason"] = State == LegState.Answered ? "hangup" : "cancelled" });
        RaiseEnded("hangup");
    }

    public override void SetHold(bool hold)
    {
        base.SetHold(hold);
        _connection.Send(new JsonObject { ["t"] = "state", ["leg"] = Id, ["state"] = hold ? "held" : "connected" });
    }

    /// <summary>The far side of the call put this browser on hold or took it off (the other party pressed hold).</summary>
    public void NotifyRemoteHold(bool hold)
        => _connection.Send(new JsonObject { ["t"] = "state", ["leg"] = Id, ["state"] = hold ? "remote-held" : "connected" });

}
