using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class TeamInput
{
    [Required(ErrorMessage = "Teams.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    [StringLength(500, ErrorMessage = "Common.Validation.TooLong")]
    public string? Description { get; set; }

    public string? Color { get; set; }

    /// <summary>The members as the picker posts them ("3, 7, 12").</summary>
    public string? Members { get; set; }
}

/// <summary>Teams: people who see each other in the panel of the web client. Only administrators change them; everybody signed in sees them.</summary>
public static partial class Teams
{
    public static IQueryable<Team> Query(MpDbContext db, Actor actor)
    {
        actor.RequireSignedIn();
        return db.Team.AsNoTracking().Include(t => t.Members);
    }

    /// <summary>All teams by name (the filter of the colleagues in the web client).</summary>
    public static async Task<List<Team>> ListAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
        => await Query(db, actor).OrderBy(t => t.Name).ToListAsync(ct);

    public static IQueryable<Team> Search(IQueryable<Team> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(t => EF.Functions.ILike(t.Name, like) || (t.Description != null && EF.Functions.ILike(t.Description, like)));
    }

    public static async Task<Team?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Team.AsNoTracking().Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public static TeamInput ToInput(Team team) => new()
    {
        Name = team.Name,
        Description = team.Description,
        Color = team.Color,
        Members = IdList.Join(team.Members.Select(m => m.UserId)),
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, TeamInput input, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var name = input.Name.Trim();
        var color = string.IsNullOrWhiteSpace(input.Color) ? null : input.Color.Trim().ToLowerInvariant();
        if (color is not null && !ColorPattern().IsMatch(color))
        {
            return SaveResult.Fail(nameof(TeamInput.Color), Text.T("Teams.Color.Invalid"));
        }

        if (await db.Team.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower(), ct))
        {
            return SaveResult.Fail(nameof(TeamInput.Name), Text.T("Teams.Name.Taken"));
        }

        var team = id is { } existing ? await db.Team.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == existing, ct) : new Team();
        if (team is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        team.Name = name;
        team.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        team.Color = color;
        var chosen = IdList.Parse(input.Members);
        var wanted = await db.User.Where(u => chosen.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        team.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        foreach (var userId in wanted.Where(w => team.Members.All(m => m.UserId != w)))
        {
            team.Members.Add(new TeamMember { UserId = userId });
        }

        if (id is null)
        {
            db.Team.Add(team);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Teams.Log.Created" : "Teams.Log.Changed"), nameof(Team), team.Id, team.Name);
        TelephonySignals.Notify(TelephonyChange.Users);
        return SaveResult.Saved(team.Id);
    }

    public static async Task DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var team = await db.Team.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            return;
        }

        db.Team.Remove(team);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Teams.Log.Deleted"), nameof(Team), team.Id, team.Name);
        TelephonySignals.Notify(TelephonyChange.Users);
    }

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex ColorPattern();
}
