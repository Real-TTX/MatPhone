using System.ComponentModel.DataAnnotations;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class CallGroupInput
{
    [Required(ErrorMessage = "Groups.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    [StringLength(500, ErrorMessage = "Common.Validation.TooLong")]
    public string? Description { get; set; }

    public RingStrategy Strategy { get; set; } = RingStrategy.Parallel;

    [Range(5, 300, ErrorMessage = "Groups.RingSeconds.Range")]
    public int RingSeconds { get; set; } = 15;

    [Range(5, 3600, ErrorMessage = "Groups.MaxWaitSeconds.Range")]
    public int MaxWaitSeconds { get; set; } = 60;

    public FallbackKind Fallback { get; set; } = FallbackKind.Hangup;

    public long? FallbackUserId { get; set; }

    public long? FallbackGroupId { get; set; }

    public long? FallbackMailboxId { get; set; }

    public string? FallbackNumber { get; set; }

    [StringLength(30, ErrorMessage = "Common.Validation.TooLong")]
    public string? CallerNamePrefix { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Who belongs to the group (the picker: "3, 7, 12").</summary>
    public string? Members { get; set; }

    /// <summary>The order of the members that were there, as the list posts them (one field per row, in the order of the rows).</summary>
    public List<long> Order { get; set; } = [];

    /// <summary>The members of the list that are logged in (a check per row).</summary>
    public List<long> LoggedIn { get; set; } = [];
}

/// <summary>What the engine and the panels need to know about a member of a group.</summary>
public sealed record GroupMembership(long GroupId, string GroupName, string? Number, bool IsLoggedIn);

/// <summary>Call groups: a number rings their logged-in members by a strategy. Administrators change them; members log themselves in and out.</summary>
public static class CallGroups
{
    public static IQueryable<CallGroup> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.CallGroup.AsNoTracking().Include(g => g.Members);
    }

    public static IQueryable<CallGroup> Search(IQueryable<CallGroup> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(g => EF.Functions.ILike(g.Name, like) || (g.Description != null && EF.Functions.ILike(g.Description, like)));
    }

    public static async Task<CallGroup?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.CallGroup.AsNoTracking().Include(g => g.Members.OrderBy(m => m.Position)).ThenInclude(m => m.User).FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    /// <summary>The numbers that ring the group (shown on its page; they are changed on the page of the number).</summary>
    public static async Task<List<PhoneNumber>> NumbersAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.PhoneNumber.AsNoTracking().Where(n => n.CallGroupId == id && n.Target == TargetKind.Group).OrderBy(n => n.Kind).ThenBy(n => n.Number).ToListAsync(ct);
    }

    public static CallGroupInput ToInput(CallGroup g) => new()
    {
        Name = g.Name,
        Description = g.Description,
        Strategy = g.Strategy,
        RingSeconds = g.RingSeconds,
        MaxWaitSeconds = g.MaxWaitSeconds,
        Fallback = g.Fallback,
        FallbackUserId = g.FallbackUserId,
        FallbackGroupId = g.FallbackGroupId,
        FallbackMailboxId = g.FallbackMailboxId,
        FallbackNumber = g.FallbackNumber,
        CallerNamePrefix = g.CallerNamePrefix,
        IsActive = g.IsActive,
        Members = IdList.Join(g.Members.OrderBy(m => m.Position).Select(m => m.UserId)),
        Order = [.. g.Members.OrderBy(m => m.Position).Select(m => m.UserId)],
        LoggedIn = [.. g.Members.Where(m => m.IsLoggedIn).Select(m => m.UserId)],
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, CallGroupInput input, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        var name = input.Name.Trim();
        if (await db.CallGroup.AnyAsync(g => g.Id != id && g.Name.ToLower() == name.ToLower(), ct))
        {
            result.Add(nameof(CallGroupInput.Name), Text.T("Groups.Name.Taken"));
        }

        await Targets.CheckFallbackAsync(db, input.Fallback, input.FallbackUserId, input.FallbackGroupId, input.FallbackMailboxId, input.FallbackNumber, id, result, ct);
        if (!result.Ok)
        {
            return result;
        }

        var group = id is { } existing ? await db.CallGroup.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == existing, ct) : new CallGroup();
        if (group is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        group.Name = name;
        group.Description = Clean(input.Description);
        group.Strategy = input.Strategy;
        group.RingSeconds = input.RingSeconds;
        group.MaxWaitSeconds = input.MaxWaitSeconds;
        group.Fallback = input.Fallback;
        group.FallbackUserId = input.Fallback == FallbackKind.User ? input.FallbackUserId : null;
        group.FallbackGroupId = input.Fallback == FallbackKind.Group ? input.FallbackGroupId : null;
        group.FallbackMailboxId = input.Fallback == FallbackKind.Mailbox ? input.FallbackMailboxId : null;
        group.FallbackNumber = input.Fallback == FallbackKind.External ? Core.Telephony.NumberRules.Clean(input.FallbackNumber) : null;
        group.CallerNamePrefix = Clean(input.CallerNamePrefix);
        group.IsActive = input.IsActive;

        var chosen = IdList.Parse(input.Members);
        var valid = await db.User.Where(u => chosen.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        group.Members.RemoveAll(m => !valid.Contains(m.UserId));
        // The members that were there keep the order of the rows and their login; new ones come at the end, logged in.
        var order = input.Order.Where(valid.Contains).Distinct().ToList();
        foreach (var member in group.Members)
        {
            member.IsLoggedIn = id is null || input.LoggedIn.Contains(member.UserId);
        }

        foreach (var userId in valid.Where(v => group.Members.All(m => m.UserId != v)))
        {
            group.Members.Add(new CallGroupMember { UserId = userId, IsLoggedIn = true });
            order.Add(userId);
        }

        foreach (var member in group.Members)
        {
            var index = order.IndexOf(member.UserId);
            member.Position = index < 0 ? order.Count : index;
        }

        if (id is null)
        {
            db.CallGroup.Add(group);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Groups.Log.Created" : "Groups.Log.Changed"), nameof(CallGroup), group.Id, group.Name);
        TelephonySignals.Notify(TelephonyChange.Groups);
        return SaveResult.Saved(group.Id);
    }

    /// <summary>A group that a number, a rule or another group still sends calls to is not deleted: the page names what refers to it.</summary>
    public static async Task<string?> DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var group = await db.CallGroup.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
        {
            return null;
        }

        if (await db.PhoneNumber.AnyAsync(n => n.CallGroupId == id && n.Target == TargetKind.Group, ct))
        {
            return Text.T("Groups.Delete.HasNumbers");
        }

        if (await db.Rule.AnyAsync(r => r.TargetGroupId == id && r.Target == TargetKind.Group, ct) || await db.CallGroup.AnyAsync(g => g.FallbackGroupId == id && g.Fallback == FallbackKind.Group, ct))
        {
            return Text.T("Groups.Delete.InUse");
        }

        db.CallGroup.Remove(group);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Groups.Log.Deleted"), nameof(CallGroup), group.Id, group.Name);
        TelephonySignals.Notify(TelephonyChange.Groups);
        return null;
    }

    /// <summary>The groups of a person with their internal number and whether the person is logged in.</summary>
    public static async Task<List<GroupMembership>> MembershipsAsync(MpDbContext db, Actor actor, long userId, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        return await db.CallGroupMember.AsNoTracking().Where(m => m.UserId == userId && m.CallGroup!.UpdateState != UpdateState.Deleted)
            .OrderBy(m => m.CallGroup!.Name)
            .Select(m => new GroupMembership(m.CallGroupId, m.CallGroup!.Name,
                db.PhoneNumber.Where(n => n.CallGroupId == m.CallGroupId && n.Target == TargetKind.Group && n.Kind == NumberKind.Internal).Select(n => n.Number).FirstOrDefault(),
                m.IsLoggedIn))
            .ToListAsync(ct);
    }

    /// <summary>Logs a member in or out of a group ("Gruppe ein/aus"): the member or an administrator. Null: the person is no member.</summary>
    public static async Task<bool?> SetLoggedInAsync(MpDbContext db, Actor actor, long userId, long groupId, bool? loggedIn, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var member = await db.CallGroupMember.FirstOrDefaultAsync(m => m.UserId == userId && m.CallGroupId == groupId, ct);
        if (member is null)
        {
            return null;
        }

        member.IsLoggedIn = loggedIn ?? !member.IsLoggedIn;
        await db.SaveChangesAsync(ct);
        TelephonySignals.NotifyUserState(userId);
        return member.IsLoggedIn;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
