using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace MatPhone.Telephony;

public enum LegKind
{
    /// <summary>A SIP phone of a user.</summary>
    Device,

    /// <summary>The web client of a user in a browser.</summary>
    Web,

    /// <summary>A line to a provider.</summary>
    Line,
}

public enum LegState
{
    New,
    Ringing,
    Answered,
    Ended,
}

/// <summary>Who calls, as the phone of the called party shows it.</summary>
public sealed record CallerId(string? Number, string? Name, string? CalledNumber = null, long? UserId = null);

/// <summary>
/// One side of a call: a SIP phone, a line, or a browser. A leg is either the caller (it is answered or rejected) or a callee (it rings and answers or not);
/// once answered it has <see cref="Media"/>, and <see cref="Ended"/> says when its far end hung up.
/// </summary>
public abstract class Leg
{
    private int _ended;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];

    public required LegKind Kind { get; init; }

    public long? UserId { get; init; }

    public long? DeviceId { get; init; }

    public long? LineId { get; init; }

    /// <summary>What the logs and the panels call it ("Yealink Büro", "Browser", "Telekom").</summary>
    public string Label { get; init; } = "";

    public LegState State { get; protected set; } = LegState.New;

    public MediaPort? Media { get; protected set; }

    /// <summary>The far end hung up, cancelled or failed; the string is the reason for the log.</summary>
    public event Action<Leg, string>? Ended;

    /// <summary>The far end started to ring (a callee).</summary>
    public event Action<Leg>? Ringing;

    public bool IsEnded => State == LegState.Ended;

    protected void RaiseRinging()
    {
        if (State == LegState.New)
        {
            State = LegState.Ringing;
        }

        Ringing?.Invoke(this);
    }

    protected void RaiseEnded(string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1)
        {
            return;
        }

        State = LegState.Ended;
        Media?.Dispose();
        Ended?.Invoke(this, reason);
    }

    // ---- as callee ----

    /// <summary>Rings the far end until it answers (true), refuses or fails (false), or <paramref name="ct"/> stops it (false, the ringing is cancelled).</summary>
    public virtual Task<bool> RingAsync(CallerId caller, CancellationToken ct) => Task.FromResult(false);

    // ---- as caller ----

    /// <summary>Tells the caller that it rings.</summary>
    public virtual void Progress()
    {
    }

    /// <summary>Answers the caller: from now on there is audio.</summary>
    public virtual Task<bool> AnswerAsync() => Task.FromResult(State == LegState.Answered);

    /// <summary>Refuses the caller with a SIP status (486 busy, 404 not found, 480 not reachable ...).</summary>
    public virtual void Reject(int status, string reason) => RaiseEnded(reason);

    // ---- both ----

    /// <summary>Ends the leg from this side (a BYE, a CANCEL, a message to the browser).</summary>
    public abstract void Hangup();

    /// <summary>Whether an in-dialog request (BYE, re-INVITE, REFER, INFO) belongs to this leg.</summary>
    public virtual bool Matches(SIPRequest request) => false;

    /// <summary>Puts the far end on hold or takes it off (for the web client; a SIP phone does it itself with a re-INVITE).</summary>
    public virtual void SetHold(bool hold)
    {
        if (Media is { } media)
        {
            media.Muted = hold;
        }
    }

    public void Close(string reason) => RaiseEnded(reason);
}

/// <summary>What the SIP legs share: the transport, the audio ports and the address written into the SDP.</summary>
public sealed class SipContext(SIPTransport transport, MediaPorts ports, TelephonyLogWriter log)
{
    public SIPTransport Transport { get; } = transport;

    public MediaPorts Ports { get; } = ports;

    public TelephonyLogWriter Log { get; } = log;

    public IPAddress? PublicAddress => Ports.PublicAddress;
}

/// <summary>A caller over SIP (a phone of this installation or a line): the INVITE that came in.</summary>
public sealed class SipInboundLeg : Leg
{
    private readonly SipContext _context;
    private readonly SIPServerUserAgent _uas;
    private SIPDialogue? _dialogue;

    public SipInboundLeg(SipContext context, SIPServerUserAgent uas)
    {
        _context = context;
        _uas = uas;
        Media = new MediaPort(MediaPort.CreateRtp(context.Ports));
        uas.CallCancelled += (_, _) => RaiseEnded("cancelled");
    }

    public SIPRequest Invite => _uas.CallRequest;

    public string CallId => Invite.Header.CallId;

    /// <summary>Takes the offer of the INVITE; false when there is no audio both sides speak (the leg is then refused with 488).</summary>
    public bool AcceptOffer()
    {
        if (string.IsNullOrWhiteSpace(Invite.Body))
        {
            return false;
        }

        var offer = SDP.ParseSDPDescription(Invite.Body);
        return Media!.Session.SetRemoteDescription(SdpType.offer, offer) == SetDescriptionResultEnum.OK;
    }

    public override void Progress()
    {
        if (State == LegState.New)
        {
            State = LegState.Ringing;
            _uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
        }
    }

    public override async Task<bool> AnswerAsync()
    {
        if (State == LegState.Answered)
        {
            return true;
        }

        if (IsEnded)
        {
            return false;
        }

        var answer = Media!.Session.CreateAnswer(_context.PublicAddress);
        _dialogue = _uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answer.ToString(), SIPDialogueTransferModesEnum.NotAllowed);
        if (_dialogue is null)
        {
            return false;
        }

        State = LegState.Answered;
        await Media.Session.Start().ConfigureAwait(false);
        return true;
    }

    public override void Reject(int status, string reason)
    {
        if (State != LegState.Answered && !IsEnded)
        {
            _uas.Reject((SIPResponseStatusCodesEnum)status, reason);
        }

        RaiseEnded(reason);
    }

    public override void Hangup()
    {
        if (IsEnded)
        {
            return;
        }

        if (State == LegState.Answered)
        {
            try
            {
                _uas.Hangup(false);
            }
            catch (Exception ex)
            {
                _context.Log.Warning(Data.Entities.LogCategory.Call, "BYE could not be sent", ex.Message);
            }
        }
        else
        {
            _uas.Reject(SIPResponseStatusCodesEnum.TemporarilyUnavailable, null);
        }

        RaiseEnded("hangup");
    }

    public override bool Matches(SIPRequest request)
        => _dialogue is { } d && request.Header.CallId == d.CallId && request.Header.To?.ToTag == d.LocalTag;

    public SIPDialogue? Dialogue => _dialogue;

    /// <summary>The far end said BYE.</summary>
    public void RemoteHungUp() => RaiseEnded("bye");
}

/// <summary>Where an outgoing SIP leg goes: the request URI, the socket to send it to, who it is from and the credentials (a line).</summary>
public sealed record SipTarget(string Uri, SIPEndPoint? SendTo, string From, string To, string? UserName, string? AuthUserName, string? Password, List<string> Headers);

/// <summary>A callee over SIP: a phone of this installation (at the address it registered from) or a number out over a line.</summary>
public sealed class SipOutboundLeg(SipContext context, SipTarget target) : Leg
{
    private SIPClientUserAgent? _uac;

    public int? FailureStatus { get; private set; }

    public override async Task<bool> RingAsync(CallerId caller, CancellationToken ct)
    {
        var media = new MediaPort(MediaPort.CreateRtp(context.Ports));
        Media = media;
        var offer = media.Session.CreateOffer(context.PublicAddress);
        var descriptor = new SIPCallDescriptor(target.UserName ?? SIPConstants.SIP_DEFAULT_USERNAME, target.Password, target.Uri, target.From, target.To, null,
            target.Headers, target.AuthUserName, SIPCallDirection.Out, SDP.SDP_MIME_CONTENTTYPE, offer.ToString(), null);
        var uac = new SIPClientUserAgent(context.Transport, target.SendTo);
        _uac = uac;
        var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        uac.CallRinging += (_, _) => RaiseRinging();
        uac.CallAnswered += (_, response) =>
        {
            if (response.StatusCode is >= 200 and <= 299)
            {
                var result = !string.IsNullOrWhiteSpace(response.Body)
                    ? media.Session.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(response.Body))
                    : SetDescriptionResultEnum.Error;
                answered.TrySetResult(result == SetDescriptionResultEnum.OK);
            }
            else
            {
                FailureStatus = response.StatusCode;
                answered.TrySetResult(false);
            }
        };
        uac.CallFailed += (_, error, response) =>
        {
            FailureStatus = response?.StatusCode;
            context.Log.Info(Data.Entities.LogCategory.Call, $"Call to {Label} failed: {error}", target.Uri, LineId is null ? null : response?.RemoteSIPEndPoint?.ToString(), LineId, DeviceId, UserId);
            answered.TrySetResult(false);
        };

        uac.Call(descriptor);
        using (ct.Register(() =>
        {
            if (answered.TrySetResult(false))
            {
                SafeCancel(uac);
            }
        }))
        {
            var ok = await answered.Task.ConfigureAwait(false);
            if (!ok)
            {
                if (uac.IsUACAnswered)
                {
                    // Answered with an SDP we cannot use: end it rather than leave a silent call.
                    uac.Hangup();
                }

                RaiseEnded(ct.IsCancellationRequested ? "cancelled" : "failed");
                return false;
            }
        }

        State = LegState.Answered;
        await media.Session.Start().ConfigureAwait(false);
        return true;
    }

    public override void Hangup()
    {
        if (IsEnded)
        {
            return;
        }

        if (_uac is { } uac)
        {
            if (State == LegState.Answered)
            {
                uac.Hangup();
            }
            else
            {
                SafeCancel(uac);
            }
        }

        RaiseEnded("hangup");
    }

    public override bool Matches(SIPRequest request)
        => _uac?.SIPDialogue is { } d && request.Header.CallId == d.CallId && request.Header.To?.ToTag == d.LocalTag;

    public SIPDialogue? Dialogue => _uac?.SIPDialogue;

    public void RemoteHungUp() => RaiseEnded("bye");

    private static void SafeCancel(SIPClientUserAgent uac)
    {
        try
        {
            uac.Cancel();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            // Nothing was sent yet, or the far end answered at the same moment: the session sees the result.
        }
    }
}
