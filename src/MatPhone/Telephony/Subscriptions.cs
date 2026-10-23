using System.Collections.Concurrent;
using System.Security;
using MatPhone.Data;
using MatPhone.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;

namespace MatPhone.Telephony;

/// <summary>
/// The subscriptions of the phones: the busy lamp field (event "dialog": a colleague is free, it rings, on the phone) and the message lamp (event
/// "message-summary": new messages in the own mailbox). The phone subscribes, the engine answers and sends a NOTIFY whenever the state changes.
/// </summary>
public sealed class Subscriptions(SIPTransport transport, TelephonyLogWriter log, IServiceScopeFactory scopes, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

    private sealed class Subscription
    {
        public required string Key { get; init; }

        public required string Event { get; init; }

        public required SIPDialogue Dialogue { get; init; }

        public required SIPEndPoint RemoteEndPoint { get; init; }

        /// <summary>Dialog: the colleague watched. Message summary: the owner of the mailbox.</summary>
        public required long UserId { get; init; }

        public required string Entity { get; init; }

        public DateTime Expires { get; set; }

        public int Version { get; set; }
    }

    /// <summary>Answers a SUBSCRIBE of an authenticated phone and sends the first NOTIFY.</summary>
    public async Task SubscribeAsync(PbxSnapshot snapshot, DeviceInfo device, SIPRequest request, SIPEndPoint remote, PresenceTracker presence)
    {
        var @event = (request.Header.Event ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        long? watched = @event switch
        {
            "dialog" => snapshot.Numbers.TryGetValue(request.URI.User ?? string.Empty, out var number) && number.Target == TargetKind.User ? number.UserId : null,
            "message-summary" => device.UserId,
            _ => null,
        };
        if (@event is not ("dialog" or "message-summary"))
        {
            var bad = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.BadEvent, null);
            bad.Header.AllowEvents = "dialog, message-summary";
            await transport.SendResponseAsync(bad).ConfigureAwait(false);
            return;
        }

        if (watched is not { } userId)
        {
            await transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.NotFound, null)).ConfigureAwait(false);
            return;
        }

        var expires = (int)Math.Clamp(request.Header.Expires < 0 ? 3600 : request.Header.Expires, 0, 3600);
        var key = $"{request.Header.CallId}|{request.Header.From.FromTag}";
        var ok = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null);
        ok.Header.Expires = expires;
        if (_subscriptions.TryGetValue(key, out var existing))
        {
            ok.Header.To.ToTag = existing.Dialogue.LocalTag;
            await transport.SendResponseAsync(ok).ConfigureAwait(false);
            existing.Expires = clock.GetUtcNow().UtcDateTime.AddSeconds(expires);
            existing.Dialogue.CSeq = Math.Max(existing.Dialogue.CSeq, 1);
            if (expires == 0)
            {
                _subscriptions.TryRemove(key, out _);
            }

            await NotifyAsync(existing, snapshot, presence, terminated: expires == 0).ConfigureAwait(false);
            return;
        }

        var toTag = CallProperties.CreateNewTag();
        ok.Header.To.ToTag = toTag;
        ok.Header.Contact = [SIPContactHeader.GetDefaultSIPContactHeader(request.URI.Scheme)];
        await transport.SendResponseAsync(ok).ConfigureAwait(false);
        var subscription = new Subscription
        {
            Key = key,
            Event = @event,
            Dialogue = new SIPDialogue(request, toTag),
            RemoteEndPoint = remote.CopyOf(),
            UserId = userId,
            Entity = $"sip:{request.URI.User}@{request.URI.Host}",
            Expires = clock.GetUtcNow().UtcDateTime.AddSeconds(expires),
        };
        if (expires > 0)
        {
            _subscriptions[key] = subscription;
        }

        await NotifyAsync(subscription, snapshot, presence, terminated: expires == 0).ConfigureAwait(false);
    }

    /// <summary>The state of a colleague changed: every phone that watches it gets a NOTIFY.</summary>
    public async Task PresenceChangedAsync(long userId, PbxSnapshot snapshot, PresenceTracker presence)
    {
        foreach (var subscription in Active().Where(s => s.Event == "dialog" && s.UserId == userId))
        {
            await NotifyAsync(subscription, snapshot, presence).ConfigureAwait(false);
        }
    }

    /// <summary>The messages of a person changed: the message lamp of the person's phones is set again.</summary>
    public async Task MessagesChangedAsync(long userId, PbxSnapshot snapshot, PresenceTracker presence)
    {
        foreach (var subscription in Active().Where(s => s.Event == "message-summary" && s.UserId == userId))
        {
            await NotifyAsync(subscription, snapshot, presence).ConfigureAwait(false);
        }
    }

    private IEnumerable<Subscription> Active()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var expired in _subscriptions.Values.Where(s => s.Expires <= now))
        {
            _subscriptions.TryRemove(expired.Key, out _);
        }

        return [.. _subscriptions.Values];
    }

    private async Task NotifyAsync(Subscription subscription, PbxSnapshot snapshot, PresenceTracker presence, bool terminated = false)
    {
        try
        {
            var notify = subscription.Dialogue.GetInDialogRequest(SIPMethodsEnum.NOTIFY);
            notify.Header.Event = subscription.Event;
            var left = (int)Math.Max(0, (subscription.Expires - clock.GetUtcNow().UtcDateTime).TotalSeconds);
            notify.Header.SubscriptionState = terminated ? "terminated;reason=timeout" : $"active;expires={left}";
            if (subscription.Event == "dialog")
            {
                notify.Header.ContentType = "application/dialog-info+xml";
                notify.Body = DialogInfo(subscription, presence.Of(subscription.UserId));
            }
            else
            {
                notify.Header.ContentType = "application/simple-message-summary";
                notify.Body = await MessageSummaryAsync(snapshot, subscription.UserId).ConfigureAwait(false);
            }

            var tx = new SIPNonInviteTransaction(transport, notify, subscription.RemoteEndPoint);
            tx.NonInviteTransactionFinalResponseReceived += (_, _, _, response) =>
            {
                if (response.StatusCode is 481 or 404)
                {
                    // The phone forgot the subscription (it restarted): no more NOTIFYs to it.
                    _subscriptions.TryRemove(subscription.Key, out _);
                }

                return Task.FromResult(System.Net.Sockets.SocketError.Success);
            };
            tx.SendRequest();
        }
        catch (Exception ex)
        {
            log.Warning(LogCategory.Registration, "A NOTIFY could not be sent", ex.Message, subscription.RemoteEndPoint.ToString());
        }
    }

    private static string DialogInfo(Subscription subscription, PresenceState state)
    {
        subscription.Version++;
        var entity = SecurityElement.Escape(subscription.Entity);
        var dialog = state switch
        {
            PresenceState.Ringing => "<dialog id=\"1\" direction=\"recipient\"><state>early</state></dialog>",
            PresenceState.Busy => "<dialog id=\"1\"><state>confirmed</state></dialog>",
            _ => string.Empty,
        };
        return $"<?xml version=\"1.0\"?>\r\n<dialog-info xmlns=\"urn:ietf:params:xml:ns:dialog-info\" version=\"{subscription.Version}\" state=\"full\" entity=\"{entity}\">{dialog}</dialog-info>\r\n";
    }

    private async Task<string> MessageSummaryAsync(PbxSnapshot snapshot, long userId)
    {
        var mailbox = snapshot.Users.TryGetValue(userId, out var user) ? user.MailboxId : null;
        var (unheard, heard) = (0, 0);
        if (mailbox is { } id)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            unheard = await db.MailboxMessage.CountAsync(m => m.MailboxId == id && m.IsNew).ConfigureAwait(false);
            heard = await db.MailboxMessage.CountAsync(m => m.MailboxId == id && !m.IsNew).ConfigureAwait(false);
        }

        return $"Messages-Waiting: {(unheard > 0 ? "yes" : "no")}\r\nMessage-Account: sip:{snapshot.Settings.MailboxCode}@matphone\r\nVoice-Message: {unheard}/{heard}\r\n";
    }
}
