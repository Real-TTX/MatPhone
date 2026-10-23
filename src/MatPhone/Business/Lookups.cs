using MatPhone.Data;
using MatPhone.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

/// <summary>An entry of a choice (a picker or a select): the key, the name and a second line.</summary>
public sealed record Choice(long Id, string Name, string? Sub = null);

/// <summary>The lists the forms choose from: users, groups, mailboxes, lines, numbers, key templates, line profiles. Sorted by name; deleted records are never offered.</summary>
public static class Lookups
{
    public static async Task<List<Choice>> UsersAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireSignedIn();
        return await db.User.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new Choice(u.Id, u.DisplayName, u.Email)).ToListAsync(ct);
    }

    public static async Task<List<Choice>> GroupsAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireSignedIn();
        return await db.CallGroup.AsNoTracking().OrderBy(g => g.Name).Select(g => new Choice(g.Id, g.Name, g.Description)).ToListAsync(ct);
    }

    public static async Task<List<Choice>> MailboxesAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Mailbox.AsNoTracking().OrderBy(m => m.Name)
            .Select(m => new Choice(m.Id, m.Name, m.User != null ? m.User.DisplayName : m.CallGroup != null ? m.CallGroup.Name : null)).ToListAsync(ct);
    }

    public static async Task<List<Choice>> LinesAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Line.AsNoTracking().OrderBy(l => l.Name).Select(l => new Choice(l.Id, l.Name, l.Host)).ToListAsync(ct);
    }

    public static async Task<List<Choice>> ProfilesAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.LineProfile.AsNoTracking().OrderByDescending(p => p.IsBuiltIn).ThenBy(p => p.Name).Select(p => new Choice(p.Id, p.Name, p.Host)).ToListAsync(ct);
    }

    public static async Task<List<Choice>> KeyTemplatesAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.KeyTemplate.AsNoTracking().OrderBy(t => t.Name).Select(t => new Choice(t.Id, t.Name, t.Description)).ToListAsync(ct);
    }

    /// <summary>The external numbers (inbound rules, the numbers of a line).</summary>
    public static async Task<List<Choice>> ExternalNumbersAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.PhoneNumber.AsNoTracking().Where(n => n.Kind == NumberKind.External).OrderBy(n => n.Number)
            .Select(n => new Choice(n.Id, n.Number, n.Description)).ToListAsync(ct);
    }

    /// <summary>The numbers a person may show on a call out: the external numbers that ring the person or one of the person's groups.</summary>
    public static async Task<List<Choice>> OutboundNumbersAsync(MpDbContext db, Actor actor, long userId, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var groups = db.CallGroupMember.Where(m => m.UserId == userId).Select(m => m.CallGroupId);
        return await db.PhoneNumber.AsNoTracking()
            .Where(n => n.Kind == NumberKind.External && (n.UserId == userId || (n.CallGroupId != null && groups.Contains(n.CallGroupId.Value))))
            .OrderBy(n => n.Number).Select(n => new Choice(n.Id, n.Number, n.Description)).ToListAsync(ct);
    }
}
