using System.ComponentModel.DataAnnotations;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class PhoneNumberInput
{
    [Required(ErrorMessage = "Numbers.Number.Required")]
    public string Number { get; set; } = "";

    public NumberKind Kind { get; set; } = NumberKind.Internal;

    [StringLength(200, ErrorMessage = "Common.Validation.TooLong")]
    public string? Description { get; set; }

    public long? LineId { get; set; }

    public TargetKind Target { get; set; } = TargetKind.User;

    public long? TargetUserId { get; set; }

    public long? TargetGroupId { get; set; }

    public long? TargetMailboxId { get; set; }

    public string? TargetNumber { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>The numbers of the installation and what they ring. Only administrators change them.</summary>
public static class PhoneNumbers
{
    public static NumberPlan Plan(TelephonyConfig t) => new(t.CountryCode, t.AreaCode, t.OutsideLinePrefix);

    public static IQueryable<PhoneNumber> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.PhoneNumber.AsNoTracking().Include(n => n.User).Include(n => n.CallGroup).Include(n => n.Mailbox).Include(n => n.Line);
    }

    public static IQueryable<PhoneNumber> Search(IQueryable<PhoneNumber> query, string text)
    {
        var like = Like.Contains(text);
        var digits = NumberRules.Clean(text);
        var digitsLike = digits.Length > 0 ? Like.Contains(digits.TrimStart('+', '0')) : like;
        return query.Where(n => EF.Functions.ILike(n.Number, digitsLike) || (n.Description != null && EF.Functions.ILike(n.Description, like))
            || (n.User != null && EF.Functions.ILike(n.User.DisplayName, like)) || (n.CallGroup != null && EF.Functions.ILike(n.CallGroup.Name, like)));
    }

    public static async Task<PhoneNumber?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.PhoneNumber.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
    }

    public static PhoneNumberInput ToInput(PhoneNumber n) => new()
    {
        Number = n.Number,
        Kind = n.Kind,
        Description = n.Description,
        LineId = n.LineId,
        Target = n.Target,
        TargetUserId = n.UserId,
        TargetGroupId = n.CallGroupId,
        TargetMailboxId = n.MailboxId,
        TargetNumber = n.TargetNumber,
        IsActive = n.IsActive,
    };

    /// <summary>The number as it is stored, or null with the reason in <paramref name="problem"/>.</summary>
    public static string? Normalize(string input, NumberKind kind, NumberPlan plan, out string? problem)
    {
        problem = null;
        var clean = NumberRules.Clean(input);
        if (kind == NumberKind.Internal)
        {
            if (!NumberRules.IsExtension(clean))
            {
                problem = Text.T("Numbers.Internal.Invalid");
                return null;
            }

            // An extension that begins like an outside line could never be dialled: "0" + digits goes out.
            if (plan.OutsideLinePrefix.Length > 0 && clean.StartsWith(plan.OutsideLinePrefix, StringComparison.Ordinal))
            {
                problem = Text.T("Numbers.Internal.Prefix", plan.OutsideLinePrefix);
                return null;
            }

            return clean;
        }

        var e164 = NumberRules.ToE164(clean, plan);
        if (e164 is null)
        {
            problem = Text.T("Numbers.External.Invalid");
        }

        return e164;
    }

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, PhoneNumberInput input, NumberPlan plan, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        var number = Normalize(input.Number, input.Kind, plan, out var problem);
        if (number is null)
        {
            result.Add(nameof(PhoneNumberInput.Number), problem!);
        }
        else if (await db.PhoneNumber.AnyAsync(n => n.Id != id && n.Number == number, ct))
        {
            result.Add(nameof(PhoneNumberInput.Number), Text.T("Numbers.Number.Taken"));
        }

        if (input.Kind == NumberKind.External && input.LineId is { } lineId && !await db.Line.AnyAsync(l => l.Id == lineId, ct))
        {
            result.Add(nameof(PhoneNumberInput.LineId), Text.T("Common.NotFound"));
        }

        await Targets.CheckTargetAsync(db, input.Target, input.TargetUserId, input.TargetGroupId, input.TargetMailboxId, input.TargetNumber, result, ct: ct);
        if (!result.Ok)
        {
            return result;
        }

        var entity = id is { } existing ? await db.PhoneNumber.FirstOrDefaultAsync(n => n.Id == existing, ct) : new PhoneNumber();
        if (entity is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        entity.Number = number!;
        entity.Kind = input.Kind;
        entity.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        entity.LineId = input.Kind == NumberKind.External ? input.LineId : null;
        entity.Target = input.Target;
        entity.UserId = input.Target == TargetKind.User ? input.TargetUserId : null;
        entity.CallGroupId = input.Target == TargetKind.Group ? input.TargetGroupId : null;
        entity.MailboxId = input.Target == TargetKind.Mailbox ? input.TargetMailboxId : null;
        entity.TargetNumber = input.Target == TargetKind.External ? NumberRules.Clean(input.TargetNumber) : null;
        entity.IsActive = input.IsActive;
        if (id is null)
        {
            db.PhoneNumber.Add(entity);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Numbers.Log.Created" : "Numbers.Log.Changed"), nameof(PhoneNumber), entity.Id, entity.Number);
        TelephonySignals.Notify(TelephonyChange.Numbers);
        return SaveResult.Saved(entity.Id);
    }

    public static async Task DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var entity = await db.PhoneNumber.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        // A number that was shown on calls out goes back to the default of the line for those users.
        await db.User.Where(u => u.OutboundNumberId == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.OutboundNumberId, (long?)null), ct);
        db.PhoneNumber.Remove(entity);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Numbers.Log.Deleted"), nameof(PhoneNumber), entity.Id, entity.Number);
        TelephonySignals.Notify(TelephonyChange.Numbers);
    }

    /// <summary>The next free extension from <paramref name="start"/> (the form of a new user proposes it).</summary>
    public static async Task<string> NextFreeExtensionAsync(MpDbContext db, Actor actor, int start = 200, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var taken = (await db.PhoneNumber.Where(n => n.Kind == NumberKind.Internal).Select(n => n.Number).ToListAsync(ct)).ToHashSet();
        var candidate = start;
        while (taken.Contains(candidate.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            candidate++;
        }

        return candidate.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
