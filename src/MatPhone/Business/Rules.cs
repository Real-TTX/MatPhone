using System.ComponentModel.DataAnnotations;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class RuleInput
{
    [Required(ErrorMessage = "Rules.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    public RuleDirection Direction { get; set; } = RuleDirection.Inbound;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The days as the checks post them (one value per day: 0 = Sunday ... 6 = Saturday); none: every day.</summary>
    public List<int> Days { get; set; } = [];

    public TimeOnly? TimeFrom { get; set; }

    public TimeOnly? TimeTo { get; set; }

    [StringLength(40, ErrorMessage = "Common.Validation.TooLong")]
    public string? Pattern { get; set; }

    public long? PhoneNumberId { get; set; }

    public long? CallerUserId { get; set; }

    public RuleAction Action { get; set; } = RuleAction.Route;

    public TargetKind Target { get; set; } = TargetKind.User;

    public long? TargetUserId { get; set; }

    public long? TargetGroupId { get; set; }

    public long? TargetMailboxId { get; set; }

    public string? TargetNumber { get; set; }

    public long? LineId { get; set; }

    [Range(0, 20, ErrorMessage = "Rules.Strip.Range")]
    public int StripDigits { get; set; }

    [StringLength(20, ErrorMessage = "Common.Validation.TooLong")]
    public string? Prefix { get; set; }

    public string? CallerNumber { get; set; }
}

/// <summary>The rules for calls in and out. Only administrators see or change them; the engine reads them (<see cref="Telephony.Router"/>).</summary>
public static class Rules
{
    public static IQueryable<Rule> Query(MpDbContext db, Actor actor, RuleDirection direction)
    {
        actor.RequireAdmin();
        return db.Rule.AsNoTracking().Include(r => r.PhoneNumber).Include(r => r.Line).Where(r => r.Direction == direction);
    }

    public static IQueryable<Rule> Search(IQueryable<Rule> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(r => EF.Functions.ILike(r.Name, like) || (r.Pattern != null && EF.Functions.ILike(r.Pattern, like)));
    }

    public static async Task<Rule?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Rule.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public static RuleInput ToInput(Rule r) => new()
    {
        Name = r.Name,
        Direction = r.Direction,
        IsEnabled = r.IsEnabled,
        Days = [.. TimeWindow.DaysOf(r.Days).Select(d => (int)d)],
        TimeFrom = r.TimeFrom,
        TimeTo = r.TimeTo,
        Pattern = r.Pattern,
        PhoneNumberId = r.PhoneNumberId,
        CallerUserId = r.CallerUserId,
        Action = r.Action,
        Target = r.Target,
        TargetUserId = r.TargetUserId,
        TargetGroupId = r.TargetGroupId,
        TargetMailboxId = r.TargetMailboxId,
        TargetNumber = r.TargetNumber,
        LineId = r.LineId,
        StripDigits = r.StripDigits,
        Prefix = r.Prefix,
        CallerNumber = r.CallerNumber,
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, RuleInput input, NumberPlan plan, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        var inbound = input.Direction == RuleDirection.Inbound;
        if (inbound && input.Action is not (RuleAction.Route or RuleAction.Reject))
        {
            result.Add(nameof(RuleInput.Action), Text.T("Rules.Action.Inbound"));
        }

        if (!inbound && input.Action is not (RuleAction.UseLine or RuleAction.Block))
        {
            result.Add(nameof(RuleInput.Action), Text.T("Rules.Action.Outbound"));
        }

        var pattern = string.IsNullOrWhiteSpace(input.Pattern) ? null : input.Pattern.Trim().ToUpperInvariant();
        if (!DialPattern.IsValid(pattern))
        {
            result.Add(nameof(RuleInput.Pattern), Text.T("Rules.Pattern.Invalid"));
        }

        if (input.TimeFrom.HasValue != input.TimeTo.HasValue || (input.TimeFrom is { } from && input.TimeTo is { } to && from == to))
        {
            result.Add(nameof(RuleInput.TimeTo), Text.T("Rules.Time.Both"));
        }

        if (inbound && input.Action == RuleAction.Route)
        {
            await Targets.CheckTargetAsync(db, input.Target, input.TargetUserId, input.TargetGroupId, input.TargetMailboxId, input.TargetNumber, result, ct: ct);
        }

        if (!inbound && input.Action == RuleAction.UseLine && (input.LineId is null || !await db.Line.AnyAsync(l => l.Id == input.LineId, ct)))
        {
            result.Add(nameof(RuleInput.LineId), Text.T("Rules.Line.Required"));
        }

        string? caller = null;
        if (!inbound && !string.IsNullOrWhiteSpace(input.CallerNumber))
        {
            caller = NumberRules.ToE164(input.CallerNumber, plan);
            if (caller is null)
            {
                result.Add(nameof(RuleInput.CallerNumber), Text.T("Numbers.External.Invalid"));
            }
        }

        if (!result.Ok)
        {
            return result;
        }

        var rule = id is { } existing ? await db.Rule.FirstOrDefaultAsync(r => r.Id == existing, ct) : new Rule();
        if (rule is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        if (id is null)
        {
            rule.Position = (await db.Rule.Where(r => r.Direction == input.Direction).MaxAsync(r => (int?)r.Position, ct) ?? -1) + 1;
        }

        rule.Name = input.Name.Trim();
        rule.Direction = input.Direction;
        rule.IsEnabled = input.IsEnabled;
        rule.Days = TimeWindow.Of(input.Days.Where(d => d is >= 0 and <= 6).Select(d => (DayOfWeek)d));
        // Seven days are the same as none: every day.
        if (rule.Days == 0b1111111)
        {
            rule.Days = 0;
        }

        rule.TimeFrom = input.TimeFrom;
        rule.TimeTo = input.TimeTo;
        rule.Pattern = pattern;
        rule.PhoneNumberId = inbound ? input.PhoneNumberId : null;
        rule.CallerUserId = inbound ? null : input.CallerUserId;
        rule.Action = input.Action;
        var routes = inbound && input.Action == RuleAction.Route;
        rule.Target = routes ? input.Target : TargetKind.None;
        rule.TargetUserId = routes && input.Target == TargetKind.User ? input.TargetUserId : null;
        rule.TargetGroupId = routes && input.Target == TargetKind.Group ? input.TargetGroupId : null;
        rule.TargetMailboxId = routes && input.Target == TargetKind.Mailbox ? input.TargetMailboxId : null;
        rule.TargetNumber = routes && input.Target == TargetKind.External ? NumberRules.Clean(input.TargetNumber) : null;
        var usesLine = !inbound && input.Action == RuleAction.UseLine;
        rule.LineId = usesLine ? input.LineId : null;
        rule.StripDigits = usesLine ? input.StripDigits : 0;
        rule.Prefix = usesLine && !string.IsNullOrWhiteSpace(input.Prefix) ? NumberRules.Clean(input.Prefix) : null;
        rule.CallerNumber = usesLine ? caller : null;
        if (id is null)
        {
            db.Rule.Add(rule);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Rules.Log.Created" : "Rules.Log.Changed"), nameof(Rule), rule.Id, rule.Name);
        TelephonySignals.Notify(TelephonyChange.Rules);
        return SaveResult.Saved(rule.Id);
    }

    /// <summary>Moves a rule one place up or down among the rules of its direction (the first that matches decides).</summary>
    public static async Task MoveAsync(MpDbContext db, Actor actor, long id, int delta, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var rule = await db.Rule.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return;
        }

        var all = await db.Rule.Where(r => r.Direction == rule.Direction).OrderBy(r => r.Position).ThenBy(r => r.Id).ToListAsync(ct);
        var index = all.FindIndex(r => r.Id == id);
        var target = Math.Clamp(index + delta, 0, all.Count - 1);
        if (target == index)
        {
            return;
        }

        all.RemoveAt(index);
        all.Insert(target, rule);
        for (var i = 0; i < all.Count; i++)
        {
            all[i].Position = i;
        }

        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Rules);
    }

    /// <summary>Puts the rules of a direction into the order of <paramref name="ids"/> (the list after a drag and drop).</summary>
    public static async Task ReorderAsync(MpDbContext db, Actor actor, RuleDirection direction, IReadOnlyList<long> ids, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var all = await db.Rule.Where(r => r.Direction == direction).ToListAsync(ct);
        var position = 0;
        foreach (var id in ids)
        {
            if (all.FirstOrDefault(r => r.Id == id) is { } rule)
            {
                rule.Position = position++;
            }
        }

        foreach (var rule in all.Where(r => !ids.Contains(r.Id)).OrderBy(r => r.Position))
        {
            rule.Position = position++;
        }

        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Rules);
    }

    public static async Task DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var rule = await db.Rule.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return;
        }

        db.Rule.Remove(rule);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Rules.Log.Deleted"), nameof(Rule), rule.Id, rule.Name);
        TelephonySignals.Notify(TelephonyChange.Rules);
    }

    public static RuleCandidate Candidate(Rule r) => new(r.Id, r.Position, r.IsEnabled, new TimeWindow(r.Days, r.TimeFrom, r.TimeTo), r.Pattern, r.PhoneNumberId, r.CallerUserId);
}
