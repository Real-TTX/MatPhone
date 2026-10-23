using MatPhone.Core.Telephony;
using MatPhone.Data.Entities;

namespace MatPhone.Telephony;

/// <summary>Where a call goes. The router decides it from the snapshot of the configuration; the call session carries it out.</summary>
public abstract record Route;

public sealed record RouteUser(long UserId) : Route;

public sealed record RouteGroup(long GroupId) : Route;

/// <summary>The caller leaves a message.</summary>
public sealed record RouteMailbox(long MailboxId) : Route;

/// <summary>The caller listens to a mailbox (the feature code); null: the caller has none.</summary>
public sealed record RouteMailboxAccess(long? MailboxId) : Route;

/// <summary>Out over a line: the number as the provider wants it in the request, as E.164, and the number shown to the called party.</summary>
public sealed record RouteExternal(LineInfo Line, string RequestNumber, string E164, string? CallerNumber) : Route;

public sealed record RouteFeature(FeatureKind Kind, long? GroupId = null) : Route;

/// <summary>The call is refused with a SIP status ("404 Not Found", "403 Forbidden").</summary>
public sealed record RouteReject(int Status, string Reason) : Route;

public enum FeatureKind
{
    ToggleDoNotDisturb,
    ToggleGroup,
}

/// <summary>
/// The dial plan. Inside: feature codes, then the extensions, then the emergency numbers, then outside numbers (with the prefix of the outside line, or a plus).
/// From a line: the number that was called, after the inbound rules. Out: the outbound rules choose the line, rewrite the number and the number shown.
/// </summary>
public static class Router
{
    public static readonly IReadOnlySet<string> EmergencyNumbers = new HashSet<string> { "110", "112", "116117" };

    public static Route FromInternal(PbxSnapshot s, long callerUserId, string dialled, DateTime local)
    {
        var number = NumberRules.Clean(dialled);
        if (number.Length == 0)
        {
            return new RouteReject(484, "Address Incomplete");
        }

        var settings = s.Settings;
        if (number == settings.MailboxCode)
        {
            return new RouteMailboxAccess(s.Users.TryGetValue(callerUserId, out var me) ? me.MailboxId : null);
        }

        if (number == settings.DoNotDisturbCode)
        {
            return new RouteFeature(FeatureKind.ToggleDoNotDisturb);
        }

        if (settings.GroupToggleCode.Length > 0 && number.StartsWith(settings.GroupToggleCode, StringComparison.Ordinal) && number.Length > settings.GroupToggleCode.Length)
        {
            var groupNumber = number[settings.GroupToggleCode.Length..];
            return s.Numbers.TryGetValue(groupNumber, out var g) && g.Target == TargetKind.Group && g.GroupId is { } groupId
                ? new RouteFeature(FeatureKind.ToggleGroup, groupId)
                : new RouteReject(404, "Not Found");
        }

        if (s.Numbers.TryGetValue(number, out var own) && own.Kind == NumberKind.Internal)
        {
            return Target(s, own.Target, own.UserId, own.GroupId, own.MailboxId, own.TargetNumber, callerUserId, local);
        }

        // The emergency numbers always go out, without a prefix and whatever the rules say.
        if (EmergencyNumbers.Contains(number))
        {
            return s.Lines.Count == 0 ? new RouteReject(503, "Service Unavailable") : Emergency(s, number, callerUserId);
        }

        string? e164;
        if (number.StartsWith('+') || number.StartsWith("00", StringComparison.Ordinal) && settings.OutsideLinePrefix != "0")
        {
            e164 = NumberRules.ToE164(number, s.Plan);
        }
        else if (settings.OutsideLinePrefix.Length > 0)
        {
            var outside = NumberRules.StripOutsideLine(number, s.Plan);
            e164 = outside is null ? null : NumberRules.ToE164(outside, s.Plan);
        }
        else
        {
            e164 = NumberRules.ToE164(number, s.Plan);
        }

        // A number of this installation dialled from outside form ("0" + own external number) rings inside, it does not leave and come back.
        if (e164 is not null && s.Numbers.TryGetValue(e164, out var ownExternal))
        {
            return Target(s, ownExternal.Target, ownExternal.UserId, ownExternal.GroupId, ownExternal.MailboxId, ownExternal.TargetNumber, callerUserId, local);
        }

        return e164 is null ? new RouteReject(404, "Not Found") : Outbound(s, callerUserId, e164, local);
    }

    /// <summary>A call from a line: the external number that was called (E.164) and the caller.</summary>
    public static Route FromLine(PbxSnapshot s, LineInfo line, string? calledE164, string? callerNumber, DateTime local)
    {
        NumberInfo? called = null;
        if (calledE164 is not null && s.Numbers.TryGetValue(calledE164, out var found) && found.Kind == NumberKind.External)
        {
            called = found;
        }

        // A line with one number: whatever the provider writes into the request, it is that number.
        called ??= s.Numbers.Values.Where(n => n.Kind == NumberKind.External && n.LineId == line.Id).Take(2).ToList() is [var only] ? only : null;

        var candidates = s.InboundRules.Select(Business.Rules.Candidate);
        var rule = RuleMatcher.FirstInbound(candidates, local, callerNumber, called?.Id, s.Plan) is { } match ? s.InboundRules.First(r => r.Id == match.Id) : null;
        if (rule is not null)
        {
            return rule.Action == RuleAction.Reject
                ? new RouteReject(486, "Busy Here")
                : Target(s, rule.Target, rule.TargetUserId, rule.TargetGroupId, rule.TargetMailboxId, rule.TargetNumber, null, local);
        }

        if (called is null)
        {
            return new RouteReject(404, "Not Found");
        }

        return Target(s, called.Target, called.UserId, called.GroupId, called.MailboxId, called.TargetNumber, null, local);
    }

    /// <summary>A target of a number, a rule, a forwarding or a fallback. <paramref name="perspective"/>: whose rules apply when it goes out (null: none, the first line).</summary>
    public static Route Target(PbxSnapshot s, TargetKind kind, long? userId, long? groupId, long? mailboxId, string? number, long? perspective, DateTime local) => kind switch
    {
        TargetKind.User when userId is { } u && s.Users.ContainsKey(u) => new RouteUser(u),
        TargetKind.Group when groupId is { } g && s.Groups.ContainsKey(g) => new RouteGroup(g),
        TargetKind.Mailbox when mailboxId is { } m && s.Mailboxes.ContainsKey(m) => new RouteMailbox(m),
        TargetKind.External when !string.IsNullOrWhiteSpace(number) => Forward(s, number, perspective, local),
        _ => new RouteReject(480, "Temporarily Unavailable"),
    };

    /// <summary>A forwarding to a number: an extension rings inside, anything else goes out.</summary>
    public static Route Forward(PbxSnapshot s, string number, long? perspective, DateTime local)
    {
        var clean = NumberRules.Clean(number);
        if (s.Numbers.TryGetValue(clean, out var own))
        {
            return Target(s, own.Target, own.UserId, own.GroupId, own.MailboxId, own.Target == TargetKind.External ? null : own.TargetNumber, perspective, local);
        }

        var e164 = NumberRules.ToE164(clean, s.Plan) ?? (NumberRules.StripOutsideLine(clean, s.Plan) is { } stripped ? NumberRules.ToE164(stripped, s.Plan) : null);
        return e164 is null ? new RouteReject(404, "Not Found") : Outbound(s, perspective, e164, local);
    }

    public static Route Outbound(PbxSnapshot s, long? callerUserId, string e164, DateTime local)
    {
        var national = NumberRules.Format(e164, NumberFormatKind.National, s.Plan);
        var candidates = s.OutboundRules.Select(Business.Rules.Candidate).ToList();
        var match = RuleMatcher.FirstOutbound(candidates, local, national, callerUserId) ?? RuleMatcher.FirstOutbound(candidates, local, e164, callerUserId);
        var rule = match is null ? null : s.OutboundRules.First(r => r.Id == match.Id);
        if (rule?.Action == RuleAction.Block)
        {
            return new RouteReject(403, "Forbidden");
        }

        var line = rule?.LineId is { } lineId ? s.Line(lineId) : s.Lines.FirstOrDefault();
        if (line is null)
        {
            return new RouteReject(503, "Service Unavailable");
        }

        var requestNumber = NumberRules.Format(e164, (NumberFormatKind)(int)line.NumberFormat, s.Plan);
        if (rule is not null)
        {
            requestNumber = RuleMatcher.Rewrite(requestNumber, rule.StripDigits, rule.Prefix);
        }

        return new RouteExternal(line, requestNumber, e164, rule?.CallerNumber ?? CallerNumberOf(s, callerUserId, line));
    }

    /// <summary>The number shown on a call out: the number the person chose (if it is one of theirs), else the default number of the line.</summary>
    public static string? CallerNumberOf(PbxSnapshot s, long? userId, LineInfo line)
    {
        if (userId is { } u && s.Users.TryGetValue(u, out var user) && user.OutboundNumberId is { } numberId
            && s.Numbers.Values.FirstOrDefault(n => n.Id == numberId) is { Kind: NumberKind.External } chosen)
        {
            return chosen.Number;
        }

        return line.DefaultCallerNumber ?? s.Numbers.Values.FirstOrDefault(n => n.Kind == NumberKind.External && n.LineId == line.Id)?.Number;
    }

    private static Route Emergency(PbxSnapshot s, string number, long? callerUserId)
    {
        var line = s.Lines[0];
        return new RouteExternal(line, number, number, CallerNumberOf(s, callerUserId, line));
    }
}
