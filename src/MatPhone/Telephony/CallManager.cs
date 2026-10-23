using System.Collections.Concurrent;
using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Telephony;

/// <summary>What the call manager needs from the rest of the engine: the configuration, the legs of a person or a line, the presence, the mailboxes.</summary>
public interface ICallHost
{
    PbxSnapshot Snapshot { get; }

    /// <summary>A leg for every registered phone and every open web client of the person.</summary>
    IReadOnlyList<Leg> LegsForUser(long userId, CallerId caller);

    Leg LegForLine(RouteExternal route, CallerId caller);

    PresenceTracker Presence { get; }

    MailboxApp Mailboxes { get; }

    TelephonyLogWriter Log { get; }

    IServiceScopeFactory Scopes { get; }

    TimeProvider Clock { get; }
}

/// <summary>
/// Carries out a route: rings people and groups by their rules, forwards, falls back to a mailbox, connects the audio and writes the call into the log when it
/// is over. Every call runs as one task; what changes it from outside (a transfer, a pickup, a hangup) goes through the call's state.
/// </summary>
public sealed class CallManager(ICallHost host)
{
    private const int MaxRouteDepth = 6;

    /// <summary>A call that goes out never rings longer than this.</summary>
    private const int ExternalRingSeconds = 120;

    private readonly ConcurrentDictionary<string, ActiveCall> _calls = new();

    public IReadOnlyCollection<ActiveCall> Calls => [.. _calls.Values];

    public ActiveCall? FindByLeg(Leg leg) => _calls.Values.FirstOrDefault(c => ReferenceEquals(c.A, leg) || ReferenceEquals(c.B, leg) || c.Ringing.ContainsKey(leg.Id));

    /// <summary>Starts a call of <paramref name="caller"/> that goes where <paramref name="route"/> says, and returns at once.</summary>
    public ActiveCall Start(Leg caller, CallerId callerId, Route route, CallDirection direction, string? calledNumber, long? lineId = null)
    {
        var call = new ActiveCall(caller, callerId, direction, host.Clock.GetUtcNow().UtcDateTime) { CalledNumber = calledNumber, LineId = lineId };
        _calls[call.Id] = call;
        caller.Ended += (_, _) => OnPartyEnded(call, caller);
        if (callerId.UserId is { } me)
        {
            host.Presence.Changed(me);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RouteAsync(call, call.A, route, 0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                host.Log.Error(LogCategory.Call, "A call failed", ex.ToString());
                Hangup(call.A);
                if (call.B is { } b)
                {
                    Hangup(b);
                }
            }
            finally
            {
                await FinishAsync(call).ConfigureAwait(false);
            }
        });
        return call;
    }

    // ---------------------------------------------------------------- routing

    private async Task RouteAsync(ActiveCall call, Leg caller, Route route, int depth)
    {
        if (call.IsOver || caller.IsEnded)
        {
            call.Result ??= CallResult.Cancelled;
            return;
        }

        if (depth > MaxRouteDepth)
        {
            host.Log.Warning(LogCategory.Call, "A forwarding loop was stopped", $"{call.Caller.Number} → {call.CalledNumber}");
            Refuse(call, caller, 482, "Loop Detected", CallResult.Failed);
            return;
        }

        switch (route)
        {
            case RouteReject reject:
                Refuse(call, caller, reject.Status, reject.Reason, reject.Status switch
                {
                    486 => CallResult.Busy,
                    403 => CallResult.Rejected,
                    _ => CallResult.Failed,
                });
                break;
            case RouteUser user:
                await RingUserAsync(call, caller, user.UserId, depth).ConfigureAwait(false);
                break;
            case RouteGroup group:
                await RingGroupAsync(call, caller, group.GroupId, depth).ConfigureAwait(false);
                break;
            case RouteMailbox mailbox:
                await host.Mailboxes.RecordAsync(call, caller, mailbox.MailboxId).ConfigureAwait(false);
                break;
            case RouteMailboxAccess access:
                await host.Mailboxes.ListenAsync(call, caller, access.MailboxId).ConfigureAwait(false);
                break;
            case RouteFeature feature:
                await host.Mailboxes.FeatureAsync(call, caller, feature).ConfigureAwait(false);
                break;
            case RouteExternal external:
                call.LineId ??= external.Line.Id;
                var callerId = call.Caller with { CalledNumber = external.E164 };
                var leg = host.LegForLine(external, callerId);
                var winner = await RingAsync(call, caller, [leg], TimeSpan.FromSeconds(ExternalRingSeconds)).ConfigureAwait(false);
                if (winner is null)
                {
                    var status = (leg as SipOutboundLeg)?.FailureStatus ?? 480;
                    Refuse(call, caller, status is 486 or 600 or 603 ? 486 : 480, status is 486 ? "Busy Here" : "Temporarily Unavailable",
                        status is 486 or 600 ? CallResult.Busy : CallResult.Failed);
                }
                else
                {
                    await ConnectAsync(call, caller, winner).ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task RingUserAsync(ActiveCall call, Leg caller, long userId, int depth)
    {
        var s = host.Snapshot;
        if (!s.Users.TryGetValue(userId, out var user))
        {
            Refuse(call, caller, 404, "Not Found", CallResult.Failed);
            return;
        }

        var local = Fmt.Local(host.Clock.GetUtcNow().UtcDateTime);
        call.RungUsers[userId] = true;
        if (!string.IsNullOrWhiteSpace(user.ForwardAlways))
        {
            await RouteAsync(call, caller, Router.Forward(s, user.ForwardAlways, userId, local), depth + 1).ConfigureAwait(false);
            return;
        }

        if (user.DoNotDisturb)
        {
            await NoAnswerAsync(call, caller, user, depth, busy: true).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(user.ForwardBusy) && host.Presence.IsBusy(userId))
        {
            await RouteAsync(call, caller, Router.Forward(s, user.ForwardBusy, userId, local), depth + 1).ConfigureAwait(false);
            return;
        }

        var legs = host.LegsForUser(userId, call.Caller);
        Leg? winner = null;
        if (legs.Count > 0)
        {
            winner = await RingAsync(call, caller, legs, TimeSpan.FromSeconds(Math.Clamp(user.NoAnswerSeconds, 5, 120))).ConfigureAwait(false);
        }

        if (winner is not null)
        {
            await ConnectAsync(call, caller, winner).ConfigureAwait(false);
            return;
        }

        if (!call.IsOver && !caller.IsEnded)
        {
            await NoAnswerAsync(call, caller, user, depth, busy: false).ConfigureAwait(false);
        }
    }

    /// <summary>Nobody answered (or "Nicht stören"): the forwarding on no answer, else the mailbox, else the caller hears busy or "not reachable".</summary>
    private async Task NoAnswerAsync(ActiveCall call, Leg caller, UserInfo user, int depth, bool busy)
    {
        var s = host.Snapshot;
        var local = Fmt.Local(host.Clock.GetUtcNow().UtcDateTime);
        if (!busy && !string.IsNullOrWhiteSpace(user.ForwardNoAnswer))
        {
            await RouteAsync(call, caller, Router.Forward(s, user.ForwardNoAnswer, user.Id, local), depth + 1).ConfigureAwait(false);
            return;
        }

        if (user.MailboxOnNoAnswer && user.MailboxId is { } mailbox)
        {
            await RouteAsync(call, caller, new RouteMailbox(mailbox), depth + 1).ConfigureAwait(false);
            return;
        }

        Refuse(call, caller, busy ? 486 : 480, busy ? "Busy Here" : "Temporarily Unavailable", busy ? CallResult.Busy : CallResult.Missed);
    }

    private async Task RingGroupAsync(ActiveCall call, Leg caller, long groupId, int depth)
    {
        var s = host.Snapshot;
        if (!s.Groups.TryGetValue(groupId, out var group))
        {
            Refuse(call, caller, 404, "Not Found", CallResult.Failed);
            return;
        }

        call.GroupId ??= group.Id;
        var displayed = call.Caller with { Name = (group.CallerNamePrefix ?? string.Empty) + (call.Caller.Name ?? call.Caller.Number) };
        var members = group.Members.Where(m => m.IsLoggedIn && s.Users.TryGetValue(m.UserId, out var u) && !u.DoNotDisturb && m.UserId != call.Caller.UserId).ToList();
        var deadline = host.Clock.GetUtcNow().UtcDateTime.AddSeconds(group.MaxWaitSeconds);
        Leg? winner = null;
        if (members.Count > 0)
        {
            if (group.Strategy == RingStrategy.Parallel)
            {
                var legs = members.SelectMany(m => LegsOf(call, m.UserId, displayed)).ToList();
                winner = legs.Count == 0 ? null : await RingAsync(call, caller, legs, TimeSpan.FromSeconds(group.MaxWaitSeconds)).ConfigureAwait(false);
            }
            else
            {
                var order = members.OrderBy(m => m.Position).ToList();
                if (group.Strategy == RingStrategy.RoundRobin && order.Where(m => m.LastCallDate is not null).MaxBy(m => m.LastCallDate) is { } last)
                {
                    var start = order.IndexOf(last) + 1;
                    order = [.. order.Skip(start), .. order.Take(start)];
                }

                // One after the other, round after round, until somebody answers or the time of the group is over.
                var rounds = 0;
                while (winner is null && !call.IsOver && !caller.IsEnded && host.Clock.GetUtcNow().UtcDateTime < deadline && rounds < 10)
                {
                    var rangAny = false;
                    foreach (var member in order)
                    {
                        var left = deadline - host.Clock.GetUtcNow().UtcDateTime;
                        if (left <= TimeSpan.Zero || call.IsOver || caller.IsEnded)
                        {
                            break;
                        }

                        var legs = LegsOf(call, member.UserId, displayed);
                        if (legs.Count == 0)
                        {
                            continue;
                        }

                        rangAny = true;
                        winner = await RingAsync(call, caller, legs, TimeSpan.FromSeconds(Math.Min(group.RingSeconds, left.TotalSeconds))).ConfigureAwait(false);
                        if (winner is not null)
                        {
                            break;
                        }
                    }

                    if (!rangAny)
                    {
                        break;
                    }

                    rounds++;
                }
            }
        }

        if (winner is not null)
        {
            if (winner.UserId is { } taker)
            {
                await RememberGroupCallAsync(group.Id, taker).ConfigureAwait(false);
            }

            await ConnectAsync(call, caller, winner).ConfigureAwait(false);
            return;
        }

        if (call.IsOver || caller.IsEnded)
        {
            call.Result ??= CallResult.Missed;
            return;
        }

        var local = Fmt.Local(host.Clock.GetUtcNow().UtcDateTime);
        Route fallback = group.Fallback switch
        {
            FallbackKind.Mailbox when group.FallbackMailboxId is { } m => new RouteMailbox(m),
            FallbackKind.User when group.FallbackUserId is { } u => new RouteUser(u),
            FallbackKind.Group when group.FallbackGroupId is { } g && g != group.Id => new RouteGroup(g),
            FallbackKind.External when !string.IsNullOrWhiteSpace(group.FallbackNumber) => Router.Forward(s, group.FallbackNumber, null, local),
            _ => new RouteReject(480, "Temporarily Unavailable"),
        };
        if (fallback is RouteReject)
        {
            Refuse(call, caller, 480, "Temporarily Unavailable", CallResult.Missed);
            return;
        }

        await RouteAsync(call, caller, fallback, depth + 1).ConfigureAwait(false);
    }

    private IReadOnlyList<Leg> LegsOf(ActiveCall call, long userId, CallerId caller)
    {
        call.RungUsers[userId] = true;
        return host.LegsForUser(userId, caller);
    }

    /// <summary>
    /// Rings the legs until one answers, the time is over or the caller hangs up. The first that answers wins, the others are cancelled. A pickup can add a leg
    /// while it rings (<see cref="Pickup"/>).
    /// </summary>
    private async Task<Leg?> RingAsync(ActiveCall call, Leg caller, IReadOnlyList<Leg> legs, TimeSpan timeout)
    {
        var round = call.NewRound();
        using var ringing = CancellationTokenSource.CreateLinkedTokenSource(call.Ended.Token);
        ringing.CancelAfter(timeout);
        caller.Progress();
        foreach (var leg in legs)
        {
            StartRinging(call, leg, ringing.Token);
        }

        // The legs fail one by one (a phone says busy): when none rings any more, the round is over before the time.
        _ = WatchAllFailedAsync(call, legs, ringing.Token);
        using (ringing.Token.Register(call.EndRound))
        {
            var winner = await round.ConfigureAwait(false);
            await ringing.CancelAsync().ConfigureAwait(false);
            foreach (var leg in call.Ringing.Values.Where(l => !ReferenceEquals(l, winner)))
            {
                leg.Hangup();
            }

            call.Ringing.Clear();
            return winner;
        }
    }

    private void StartRinging(ActiveCall call, Leg leg, CancellationToken ct)
    {
        call.Ringing[leg.Id] = leg;
        if (leg.UserId is { } user)
        {
            host.Presence.Changed(user);
        }

        _ = Task.Run(async () =>
        {
            bool answered;
            try
            {
                answered = await leg.RingAsync(call.Caller, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                host.Log.Warning(LogCategory.Call, $"Ringing {leg.Label} failed", ex.Message, deviceId: leg.DeviceId, userId: leg.UserId);
                answered = false;
            }

            if (answered && !call.TryWin(leg))
            {
                // Another leg was quicker (two phones answered at the same moment): this one is ended at once.
                leg.Hangup();
            }

            if (!answered)
            {
                call.Ringing.TryRemove(leg.Id, out _);
                if (leg.UserId is { } u)
                {
                    host.Presence.Changed(u);
                }
            }
        }, CancellationToken.None);
    }

    private static async Task WatchAllFailedAsync(ActiveCall call, IReadOnlyList<Leg> legs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                if (legs.All(l => l.IsEnded) && call.Ringing.IsEmpty)
                {
                    call.EndRound();
                    return;
                }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Answers the caller (if it is not answered yet) and connects the audio of the two; waits until one of them hangs up.</summary>
    private async Task ConnectAsync(ActiveCall call, Leg caller, Leg callee)
    {
        if (!await caller.AnswerAsync().ConfigureAwait(false) || caller.IsEnded)
        {
            Hangup(callee);
            call.Result ??= CallResult.Cancelled;
            return;
        }

        call.A = caller;
        call.B = callee;
        call.AnswerDate ??= host.Clock.GetUtcNow().UtcDateTime;
        call.AnsweredBy ??= callee.UserId;
        call.Result = CallResult.Answered;
        call.Bridge?.Dispose();
        call.Bridge = new MediaBridge(caller.Media!, callee.Media!);
        callee.Ended += (_, _) => OnPartyEnded(call, callee);
        Touch(caller, callee);
        await WaitUntilOverAsync(call).ConfigureAwait(false);
    }

    private static async Task WaitUntilOverAsync(ActiveCall call)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, call.Ended.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>A party hung up. While the call is connected the other one is hung up too (unless a transfer is replacing it).</summary>
    private void OnPartyEnded(ActiveCall call, Leg leg)
    {
        if (call.IsOver)
        {
            return;
        }

        if (ReferenceEquals(call.A, leg) || ReferenceEquals(call.B, leg))
        {
            if (call.B is not null && call.Other(leg) is { IsEnded: false } other)
            {
                Hangup(other);
            }

            call.Ended.Cancel();
        }

        Touch(leg);
    }

    private void Refuse(ActiveCall call, Leg caller, int status, string reason, CallResult result)
    {
        call.Result ??= result;
        if (caller.State == LegState.Answered)
        {
            Hangup(caller);
        }
        else
        {
            caller.Reject(status, reason);
        }

        call.Ended.Cancel();
    }

    public void Hangup(Leg leg)
    {
        try
        {
            leg.Hangup();
        }
        catch (Exception ex)
        {
            host.Log.Warning(LogCategory.Call, $"Hanging up {leg.Label} failed", ex.Message);
        }

        Touch(leg);
    }

    // ---------------------------------------------------------------- actions of a party

    /// <summary>
    /// A blind transfer: <paramref name="transferring"/> leaves the call, its other party goes to <paramref name="number"/> (dialled as the transferring person
    /// would dial it). The other party is already answered, so it hears nothing until the new target answers.
    /// </summary>
    public bool Transfer(Leg transferring, string number)
    {
        var call = FindByLeg(transferring);
        if (call?.Other(transferring) is not { IsEnded: false } stays || transferring.UserId is not { } userId)
        {
            return false;
        }

        var route = Router.FromInternal(host.Snapshot, userId, number, Fmt.Local(host.Clock.GetUtcNow().UtcDateTime));
        if (route is RouteReject or RouteFeature or RouteMailboxAccess)
        {
            return false;
        }

        call.Bridge?.Dispose();
        call.Bridge = null;
        // The call goes on without the person who transferred it: a new call of the party that stays, the old one ends without hanging up that party.
        var next = new ActiveCall(stays, call.Caller, call.Direction, host.Clock.GetUtcNow().UtcDateTime) { CalledNumber = number, LineId = call.LineId };
        call.A = transferring;
        call.B = null;
        Hangup(transferring);
        call.Ended.Cancel();
        _calls[next.Id] = next;
        stays.Ended += (_, _) => OnPartyEnded(next, stays);
        _ = Task.Run(async () =>
        {
            try
            {
                await RouteAsync(next, stays, route, 1).ConfigureAwait(false);
            }
            finally
            {
                await FinishAsync(next).ConfigureAwait(false);
            }
        });
        return true;
    }

    /// <summary>Takes a call that rings for <paramref name="colleague"/> ("Heranholen"): the leg of the person who picks it up answers in its place.</summary>
    public bool Pickup(long colleague, Func<CallerId, Leg?> createLeg)
    {
        var call = _calls.Values.FirstOrDefault(c => !c.IsOver && c.B is null && c.Ringing.Values.Any(l => l.UserId == colleague));
        if (call is null || createLeg(call.Caller) is not { } leg)
        {
            return false;
        }

        call.Ringing[leg.Id] = leg;
        if (leg.UserId is { } me)
        {
            call.RungUsers[me] = true;
        }

        _ = Task.Run(async () =>
        {
            if (await leg.RingAsync(call.Caller, call.Ended.Token).ConfigureAwait(false) && !call.TryWin(leg))
            {
                leg.Hangup();
            }
        });
        return true;
    }

    private void Touch(params Leg[] legs)
    {
        foreach (var user in legs.Select(l => l.UserId).Where(u => u is not null).Distinct())
        {
            host.Presence.Changed(user!.Value);
        }
    }

    private async Task RememberGroupCallAsync(long groupId, long userId)
    {
        try
        {
            using var scope = host.Scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            await db.CallGroupMember.Where(m => m.CallGroupId == groupId && m.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastCallDate, host.Clock.GetUtcNow().UtcDateTime)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            host.Log.Warning(LogCategory.Call, "The round robin of a group could not be saved", ex.Message);
        }
    }

    // ---------------------------------------------------------------- the end

    private async Task FinishAsync(ActiveCall call)
    {
        call.Ended.Cancel();
        call.Bridge?.Dispose();
        foreach (var leg in call.Ringing.Values)
        {
            Hangup(leg);
        }

        _calls.TryRemove(call.Id, out _);
        Touch(call.A);
        if (call.B is { } b)
        {
            Touch(b);
        }

        foreach (var user in call.RungUsers.Keys)
        {
            host.Presence.Changed(user);
        }

        try
        {
            var end = host.Clock.GetUtcNow().UtcDateTime;
            var record = new CallRecord
            {
                CallId = call.Id,
                Direction = call.Direction,
                StartDate = call.StartDate,
                AnswerDate = call.AnswerDate,
                EndDate = end,
                DurationSeconds = call.AnswerDate is { } answered ? (int)Math.Round((end - answered).TotalSeconds) : 0,
                CallerNumber = call.Caller.Number,
                CallerName = call.Caller.Name,
                CalledNumber = call.CalledNumber,
                Result = call.Result ?? (call.AnswerDate is null ? CallResult.Missed : CallResult.Answered),
                LineId = call.LineId,
                CallGroupId = call.GroupId,
                AnsweredByUserId = call.AnsweredBy,
            };
            if (call.Caller.UserId is { } callerUser)
            {
                record.Participants.Add(new CallParticipant { UserId = callerUser, IsCaller = true, Answered = call.AnswerDate is not null });
            }

            foreach (var user in call.RungUsers.Keys.Where(u => u != call.Caller.UserId))
            {
                record.Participants.Add(new CallParticipant { UserId = user, Answered = call.AnsweredBy == user });
            }

            using var scope = host.Scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            await CallLog.WriteAsync(db, record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            host.Log.Error(LogCategory.Call, "The call could not be written into the call log", ex.Message);
        }
    }
}
