using System.ComponentModel.DataAnnotations;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class FunctionKeyInput
{
    public KeyKind Kind { get; set; } = KeyKind.BusyLamp;

    [StringLength(40, ErrorMessage = "Common.Validation.TooLong")]
    public string? Label { get; set; }

    public long? TargetUserId { get; set; }

    public long? TargetGroupId { get; set; }

    [StringLength(40, ErrorMessage = "Common.Validation.TooLong")]
    public string? Number { get; set; }
}

public sealed class KeyTemplateInput
{
    [Required(ErrorMessage = "Keys.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    [StringLength(500, ErrorMessage = "Common.Validation.TooLong")]
    public string? Description { get; set; }

    /// <summary>The keys in their order (the rows of the editor).</summary>
    public List<FunctionKeyInput> Keys { get; set; } = [];
}

/// <summary>A key as a phone or the web client shows it: what it does and its label (the name of the colleague when the template left it empty).</summary>
public sealed record KeyView(long Id, int Position, KeyKind Kind, string Label, long? TargetUserId, long? TargetGroupId, string? Number, bool Personal);

/// <summary>
/// Templates of function keys ("Funktionstasten"): administrators make them and give them to users or devices; every person adds personal keys after those of
/// the template.
/// </summary>
public static class KeyTemplates
{
    public const int MaxKeys = 60;

    public static IQueryable<KeyTemplate> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.KeyTemplate.AsNoTracking().Include(t => t.Keys);
    }

    public static IQueryable<KeyTemplate> Search(IQueryable<KeyTemplate> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(t => EF.Functions.ILike(t.Name, like) || (t.Description != null && EF.Functions.ILike(t.Description, like)));
    }

    public static async Task<KeyTemplate?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.KeyTemplate.AsNoTracking().Include(t => t.Keys.OrderBy(k => k.Position)).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    /// <summary>How many users and devices use the template (shown on its page and in the list).</summary>
    public static async Task<(int Users, int Devices)> UsageAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return (await db.User.CountAsync(u => u.KeyTemplateId == id, ct), await db.Device.CountAsync(d => d.KeyTemplateId == id, ct));
    }

    public static KeyTemplateInput ToInput(KeyTemplate t) => new()
    {
        Name = t.Name,
        Description = t.Description,
        Keys = [.. t.Keys.OrderBy(k => k.Position).Select(ToInput)],
    };

    public static FunctionKeyInput ToInput(FunctionKey k) => new()
    {
        Kind = k.Kind,
        Label = k.Label,
        TargetUserId = k.TargetUserId,
        TargetGroupId = k.TargetGroupId,
        Number = k.Number,
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, KeyTemplateInput input, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var name = input.Name.Trim();
        if (await db.KeyTemplate.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower(), ct))
        {
            return SaveResult.Fail(nameof(KeyTemplateInput.Name), Text.T("Keys.Name.Taken"));
        }

        var result = await CheckKeysAsync(db, input.Keys, ct);
        if (!result.Ok)
        {
            return result;
        }

        var template = id is { } existing ? await db.KeyTemplate.Include(t => t.Keys).FirstOrDefaultAsync(t => t.Id == existing, ct) : new KeyTemplate();
        if (template is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        template.Name = name;
        template.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        db.FunctionKey.RemoveRange(template.Keys);
        template.Keys = [.. Build(input.Keys)];
        if (id is null)
        {
            db.KeyTemplate.Add(template);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Keys.Log.Created" : "Keys.Log.Changed"), nameof(KeyTemplate), template.Id, template.Name);
        TelephonySignals.Notify(TelephonyChange.Keys);
        return SaveResult.Saved(template.Id);
    }

    public static async Task DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var template = await db.KeyTemplate.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return;
        }

        await db.User.Where(u => u.KeyTemplateId == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.KeyTemplateId, (long?)null), ct);
        await db.Device.Where(d => d.KeyTemplateId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.KeyTemplateId, (long?)null), ct);
        db.KeyTemplate.Remove(template);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Keys.Log.Deleted"), nameof(KeyTemplate), template.Id, template.Name);
        TelephonySignals.Notify(TelephonyChange.Keys);
    }

    /// <summary>The personal keys of a person, which follow those of the template.</summary>
    public static async Task<List<FunctionKeyInput>> PersonalAsync(MpDbContext db, Actor actor, long userId, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        return await db.FunctionKey.AsNoTracking().Where(k => k.UserId == userId).OrderBy(k => k.Position).Select(k => new FunctionKeyInput
        {
            Kind = k.Kind,
            Label = k.Label,
            TargetUserId = k.TargetUserId,
            TargetGroupId = k.TargetGroupId,
            Number = k.Number,
        }).ToListAsync(ct);
    }

    public static async Task<SaveResult> SavePersonalAsync(MpDbContext db, Actor actor, long userId, List<FunctionKeyInput> keys, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var result = await CheckKeysAsync(db, keys, ct);
        if (!result.Ok)
        {
            return result;
        }

        await db.FunctionKey.Where(k => k.UserId == userId).ExecuteDeleteAsync(ct);
        foreach (var key in Build(keys))
        {
            key.UserId = userId;
            db.FunctionKey.Add(key);
        }

        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Keys);
        return SaveResult.Saved(userId);
    }

    /// <summary>
    /// The keys of a person: those of the template (of the device when it has one, else of the person) and the personal ones after them. Labels left empty take the
    /// name of the colleague or the group, or the number.
    /// </summary>
    public static async Task<List<KeyView>> EffectiveAsync(MpDbContext db, Actor actor, long userId, long? deviceTemplateId = null, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var templateId = deviceTemplateId ?? await db.User.Where(u => u.Id == userId).Select(u => u.KeyTemplateId).FirstOrDefaultAsync(ct);
        var keys = await db.FunctionKey.AsNoTracking()
            .Where(k => (templateId != null && k.KeyTemplateId == templateId) || k.UserId == userId)
            .Select(k => new
            {
                k.Id, k.Position, k.Kind, k.Label, k.TargetUserId, k.TargetGroupId, k.Number, Personal = k.UserId != null,
                UserName = k.TargetUser != null ? k.TargetUser.DisplayName : null,
                GroupName = k.TargetGroup != null ? k.TargetGroup.Name : null,
            })
            .ToListAsync(ct);
        return [.. keys.OrderBy(k => k.Personal).ThenBy(k => k.Position).Select((k, i) => new KeyView(k.Id, i, k.Kind,
            !string.IsNullOrWhiteSpace(k.Label) ? k.Label : k.UserName ?? k.GroupName ?? k.Number ?? Text.T("Labels.KeyKind." + k.Kind),
            k.TargetUserId, k.TargetGroupId, k.Number, k.Personal))];
    }

    private static async Task<SaveResult> CheckKeysAsync(MpDbContext db, List<FunctionKeyInput> keys, CancellationToken ct)
    {
        var result = new SaveResult();
        if (keys.Count > MaxKeys)
        {
            return result.Add("", Text.T("Keys.TooMany", MaxKeys));
        }

        var users = keys.Where(k => k.TargetUserId is not null).Select(k => k.TargetUserId!.Value).Distinct().ToList();
        var knownUsers = await db.User.Where(u => users.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        var groups = keys.Where(k => k.TargetGroupId is not null).Select(k => k.TargetGroupId!.Value).Distinct().ToList();
        var knownGroups = await db.CallGroup.Where(g => groups.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var field = $"Keys[{i}]";
            switch (key.Kind)
            {
                case KeyKind.BusyLamp when key.TargetUserId is not { } u || !knownUsers.Contains(u):
                    result.Add(field + ".TargetUserId", Text.T("Keys.User.Required"));
                    break;
                case KeyKind.GroupToggle when key.TargetGroupId is not { } g || !knownGroups.Contains(g):
                    result.Add(field + ".TargetGroupId", Text.T("Keys.Group.Required"));
                    break;
                case KeyKind.SpeedDial or KeyKind.Forward when Core.Telephony.NumberRules.Clean(key.Number).Length == 0:
                    result.Add(field + ".Number", Text.T("Keys.Number.Required"));
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<FunctionKey> Build(IEnumerable<FunctionKeyInput> keys)
        => keys.Select((k, i) => new FunctionKey
        {
            Position = i,
            Kind = k.Kind,
            Label = k.Label?.Trim() ?? "",
            TargetUserId = k.Kind == KeyKind.BusyLamp ? k.TargetUserId : null,
            TargetGroupId = k.Kind == KeyKind.GroupToggle ? k.TargetGroupId : null,
            Number = k.Kind is KeyKind.SpeedDial or KeyKind.Forward ? Core.Telephony.NumberRules.Clean(k.Number) : null,
        });
}
