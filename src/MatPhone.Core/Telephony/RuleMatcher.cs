namespace MatPhone.Core.Telephony;

/// <summary>A rule as the matcher sees it (copied from the data model, so that the decision can be tested without a database).</summary>
public sealed record RuleCandidate(long Id, int Position, bool IsEnabled, TimeWindow Window, string? Pattern, long? PhoneNumberId, long? CallerUserId);

/// <summary>
/// Which rule decides a call: the first enabled one by position whose conditions all hold. Inbound the pattern is matched against the caller's number (E.164 and
/// the national form, so "0151." and "+49151." both work) and the number id against the number that was called; outbound against the dialled number and who dials.
/// </summary>
public static class RuleMatcher
{
    public static RuleCandidate? FirstInbound(IEnumerable<RuleCandidate> rules, DateTime local, string? callerNumber, long? calledNumberId, NumberPlan plan)
    {
        var national = callerNumber is null ? null : NumberRules.Display(callerNumber, plan).Replace(" ", string.Empty, StringComparison.Ordinal);
        return rules.Where(r => r.IsEnabled).OrderBy(r => r.Position).ThenBy(r => r.Id).FirstOrDefault(r =>
            r.Window.Matches(local)
            && (r.PhoneNumberId is null || r.PhoneNumberId == calledNumberId)
            && (string.IsNullOrWhiteSpace(r.Pattern) || DialPattern.Matches(r.Pattern, callerNumber) || DialPattern.Matches(r.Pattern, national)));
    }

    public static RuleCandidate? FirstOutbound(IEnumerable<RuleCandidate> rules, DateTime local, string dialled, long? callerUserId)
        => rules.Where(r => r.IsEnabled).OrderBy(r => r.Position).ThenBy(r => r.Id).FirstOrDefault(r =>
            r.Window.Matches(local)
            && (r.CallerUserId is null || r.CallerUserId == callerUserId)
            && DialPattern.Matches(r.Pattern, dialled));

    /// <summary>The number an outbound rule sends: <paramref name="strip"/> digits off the front, then <paramref name="prefix"/>.</summary>
    public static string Rewrite(string number, int strip, string? prefix)
    {
        var rest = strip > 0 ? (strip >= number.Length ? string.Empty : number[strip..]) : number;
        return (prefix ?? string.Empty) + rest;
    }
}
