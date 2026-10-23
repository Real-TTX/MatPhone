using System.ComponentModel.DataAnnotations;
using MatPhone.Core.Security;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using MatPhone.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class UserInput
{
    [Required(ErrorMessage = "Admin.Users.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Admin.Users.Email.Required")]
    [EmailAddress(ErrorMessage = "Admin.Users.Email.Invalid")]
    public string Email { get; set; } = "";

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    public string? Password { get; set; }

    /// <summary>The first internal number of the person ("201"). Empty: the person has none (an existing one is removed).</summary>
    public string? Extension { get; set; }

    public bool WebClientEnabled { get; set; } = true;

    public long? KeyTemplateId { get; set; }

    public long? OutboundNumberId { get; set; }

    /// <summary>A new person gets a mailbox of their own.</summary>
    public bool CreateMailbox { get; set; } = true;

    public string Language { get; set; } = "de";
}

/// <summary>What a person sets for the own calls ("Meine Telefonie"): forwarding, "Nicht stören", the mailbox after no answer, the number shown.</summary>
public sealed class CallSettingsInput
{
    public bool DoNotDisturb { get; set; }

    public string? ForwardAlways { get; set; }

    public string? ForwardBusy { get; set; }

    public string? ForwardNoAnswer { get; set; }

    [Range(5, 120, ErrorMessage = "Telephony.NoAnswer.Range")]
    public int NoAnswerSeconds { get; set; } = 20;

    public bool MailboxOnNoAnswer { get; set; } = true;

    public long? OutboundNumberId { get; set; }
}

/// <summary>A colleague as the web client shows them: name, extension, teams.</summary>
public sealed record Colleague(long Id, string Name, string? Email, string? Extension, IReadOnlyList<long> TeamIds, bool DoNotDisturb, bool Forwarding);

/// <summary>
/// The people of the installation: their account (administrators) and their telephony. Creating, changing and deleting accounts is for administrators; the own
/// call settings are the person's (<see cref="SaveCallSettingsAsync"/>).
/// </summary>
public static class Users
{
    public static IQueryable<User> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.User.AsNoTracking().Include(u => u.Totp);
    }

    public static IQueryable<User> Search(IQueryable<User> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(u => EF.Functions.ILike(u.DisplayName, like) || EF.Functions.ILike(u.Email, like));
    }

    /// <summary>The internal numbers of the people of a page of the list (one query for the page).</summary>
    public static async Task<Dictionary<long, string>> ExtensionsAsync(MpDbContext db, Actor actor, IEnumerable<long> userIds, CancellationToken ct = default)
    {
        actor.RequireSignedIn();
        var ids = userIds.ToList();
        var rows = await db.PhoneNumber.AsNoTracking().Where(n => n.Kind == NumberKind.Internal && n.Target == TargetKind.User && n.UserId != null && ids.Contains(n.UserId.Value))
            .OrderBy(n => n.Id).Select(n => new { UserId = n.UserId!.Value, n.Number }).ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Number)));
    }

    public static async Task<User?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(id);
        return await db.User.AsNoTracking().Include(u => u.Totp).FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public static async Task<int> ActiveSessionsAsync(MpDbContext db, Actor actor, long id, DateTime now, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(id);
        return await db.UserSession.CountAsync(s => s.UserId == id && s.RevokedDate == null && s.ExpiresDate > now, ct);
    }

    public static async Task<UserInput> ToInputAsync(MpDbContext db, User u, CancellationToken ct = default) => new()
    {
        DisplayName = u.DisplayName,
        Email = u.Email,
        Role = u.Role,
        IsActive = u.IsActive,
        Extension = await PrimaryExtensionAsync(db, u.Id, ct),
        WebClientEnabled = u.WebClientEnabled,
        KeyTemplateId = u.KeyTemplateId,
        OutboundNumberId = u.OutboundNumberId,
        CreateMailbox = false,
        Language = u.Language,
    };

    public static Task<string?> PrimaryExtensionAsync(MpDbContext db, long userId, CancellationToken ct = default)
        => db.PhoneNumber.AsNoTracking().Where(n => n.UserId == userId && n.Target == TargetKind.User && n.Kind == NumberKind.Internal).OrderBy(n => n.Id)
            .Select(n => n.Number).FirstOrDefaultAsync(ct);

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, UserInput input, NumberPlan plan, SignInService signIn, SessionCache cache,
        ActivityLogger log, DateTime now, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        var email = SignInService.NormalizeEmail(input.Email);
        if (await db.User.AnyAsync(u => u.Email == email && u.Id != id, ct))
        {
            result.Add(nameof(UserInput.Email), Text.T("Admin.Users.Email.Taken"));
        }

        if (!string.IsNullOrEmpty(input.Password) && UserService.CheckPassword(input.Password, email) is { } weak)
        {
            result.Add(nameof(UserInput.Password), weak);
        }

        if (id is null && string.IsNullOrEmpty(input.Password))
        {
            result.Add(nameof(UserInput.Password), Text.T("Admin.Users.Password.Required"));
        }

        var existing = id is { } userId ? await db.User.FirstOrDefaultAsync(u => u.Id == userId, ct) : null;
        if (id is not null && existing is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        if (existing is { Role: UserRole.Admin } && (input.Role != UserRole.Admin || !input.IsActive)
            && !await db.User.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive, ct))
        {
            result.Add(nameof(UserInput.Role), Text.T("Admin.Users.LastAdmin"));
        }

        if (existing is not null && existing.Id == actor.UserId && (!input.IsActive || input.Role != UserRole.Admin))
        {
            result.Add(nameof(UserInput.Role), Text.T("Admin.Users.NotYourself"));
        }

        string? extension = null;
        if (!string.IsNullOrWhiteSpace(input.Extension))
        {
            extension = PhoneNumbers.Normalize(input.Extension, NumberKind.Internal, plan, out var problem);
            if (extension is null)
            {
                result.Add(nameof(UserInput.Extension), problem!);
            }
            else if (await db.PhoneNumber.AnyAsync(n => n.Number == extension && !(n.UserId == id && n.Target == TargetKind.User && id != null), ct))
            {
                result.Add(nameof(UserInput.Extension), Text.T("Numbers.Number.Taken"));
            }
        }

        if (input.KeyTemplateId is { } templateId && !await db.KeyTemplate.AnyAsync(t => t.Id == templateId, ct))
        {
            result.Add(nameof(UserInput.KeyTemplateId), Text.T("Common.NotFound"));
        }

        if (!result.Ok)
        {
            return result;
        }

        var user = existing ?? new User { PasswordChangedDate = now };
        var roleChanged = existing is not null && existing.Role != input.Role;
        var deactivated = existing is { IsActive: true } && !input.IsActive;
        user.DisplayName = input.DisplayName.Trim();
        user.Email = email;
        user.Role = input.Role;
        user.IsActive = input.IsActive;
        user.WebClientEnabled = input.WebClientEnabled;
        user.KeyTemplateId = input.KeyTemplateId;
        user.OutboundNumberId = input.OutboundNumberId;
        user.Language = Localization.Languages.Normalize(input.Language);
        if (!string.IsNullOrEmpty(input.Password))
        {
            user.PasswordHash = PasswordHasher.Hash(input.Password);
            user.PasswordChangedDate = now;
            user.FailedLogins = 0;
            user.LockoutEndDate = null;
        }

        if (existing is null)
        {
            db.User.Add(user);
        }

        await db.SaveChangesAsync(ct);
        await SetPrimaryExtensionAsync(db, user, extension, ct);
        if (existing is null && input.CreateMailbox && !await db.Mailbox.AnyAsync(m => m.UserId == user.Id, ct))
        {
            db.Mailbox.Add(new Mailbox { Name = user.DisplayName, UserId = user.Id });
            await db.SaveChangesAsync(ct);
        }

        cache.InvalidateUser(user.Id);
        if (existing is not null && (deactivated || !string.IsNullOrEmpty(input.Password)))
        {
            // Whoever knew the old password is out, and so is everybody of an account that may no longer sign in.
            await signIn.EndAllSessionsAsync(user.Id, null, ct);
        }

        await log.LogAsync(Text.T(existing is null ? "Admin.Users.Log.Created" : "Admin.Users.Log.Changed"), nameof(User), user.Id,
            roleChanged ? Text.T("Admin.Users.Log.NewRole", user.Email, Text.T(user.Role == UserRole.Admin ? "Labels.Role.Admin" : "Labels.Role.User")) : user.Email);
        TelephonySignals.Notify(TelephonyChange.Users);
        return SaveResult.Saved(user.Id);
    }

    /// <summary>The first internal number of the person becomes <paramref name="extension"/> (made, changed or, when empty, removed).</summary>
    private static async Task SetPrimaryExtensionAsync(MpDbContext db, User user, string? extension, CancellationToken ct)
    {
        var current = await db.PhoneNumber.Where(n => n.UserId == user.Id && n.Target == TargetKind.User && n.Kind == NumberKind.Internal).OrderBy(n => n.Id).FirstOrDefaultAsync(ct);
        if (extension is null)
        {
            if (current is not null)
            {
                db.PhoneNumber.Remove(current);
            }
        }
        else if (current is null)
        {
            db.PhoneNumber.Add(new PhoneNumber { Number = extension, Kind = NumberKind.Internal, Target = TargetKind.User, UserId = user.Id, Description = user.DisplayName });
        }
        else
        {
            current.Number = extension;
        }

        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Numbers);
    }

    public static async Task<string?> DeleteAsync(MpDbContext db, Actor actor, long id, SignInService signIn, SessionCache cache, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var user = await db.User.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return null;
        }

        if (user.Id == actor.UserId)
        {
            return Text.T("Admin.Users.CannotDeleteYourself");
        }

        if (user.Role == UserRole.Admin && user.IsActive && !await db.User.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
        {
            return Text.T("Admin.Users.OnlyAdmin");
        }

        await signIn.EndAllSessionsAsync(user.Id, null, ct);
        // The numbers of the person ring nobody now: their target is cleared, so an administrator sees them as free.
        await db.PhoneNumber.Where(n => n.UserId == id && n.Target == TargetKind.User)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Target, TargetKind.None).SetProperty(n => n.UserId, (long?)null), ct);
        await db.Device.Where(d => d.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.UserId, (long?)null), ct);
        await db.CallGroupMember.Where(m => m.UserId == id).ExecuteDeleteAsync(ct);
        await db.TeamMember.Where(m => m.UserId == id).ExecuteDeleteAsync(ct);
        db.User.Remove(user);
        await db.SaveChangesAsync(ct);
        cache.InvalidateUser(user.Id);
        await log.LogAsync(Text.T("Admin.Users.Log.Deleted"), nameof(User), user.Id, user.Email);
        TelephonySignals.Notify(TelephonyChange.Users);
        return null;
    }

    /// <summary>What the page of a person shows of the telephony: the numbers that ring the person, the devices, the own mailbox.</summary>
    public static async Task<(List<PhoneNumber> Numbers, List<Device> Devices, Mailbox? Mailbox)> OverviewAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(id);
        var numbers = await db.PhoneNumber.AsNoTracking().Where(n => n.UserId == id && n.Target == TargetKind.User).OrderBy(n => n.Kind).ThenBy(n => n.Number).ToListAsync(ct);
        var devices = await db.Device.AsNoTracking().Where(d => d.UserId == id).OrderBy(d => d.Name).ToListAsync(ct);
        var mailbox = await db.Mailbox.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == id, ct);
        return (numbers, devices, mailbox);
    }

    /// <summary>Removes the second factor of a person (an administrator, when the person lost the phone).</summary>
    public static async Task ResetTotpAsync(MpDbContext db, Actor actor, long id, TwoFactorService twoFactor, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var user = await db.User.Include(u => u.Totp).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return;
        }

        await twoFactor.DisableAsync(user);
        await log.LogAsync(Text.T("Admin.Users.Log.TotpReset"), nameof(User), user.Id, user.Email);
    }

    /// <summary>Ends the sessions of a person; <paramref name="keep"/> is the one in use when administrators end their own.</summary>
    public static async Task EndSessionsAsync(Actor actor, long id, Guid? keep, SignInService signIn, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        await signIn.EndAllSessionsAsync(id, keep, ct);
        await log.LogAsync(Text.T("Admin.Users.Log.SessionsEnded"), nameof(User), id);
    }

    public static async Task UnlockAsync(MpDbContext db, Actor actor, long id, SessionCache cache, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        await db.User.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLogins, 0).SetProperty(u => u.LockoutEndDate, (DateTime?)null), ct);
        cache.InvalidateUser(id);
    }

    public static CallSettingsInput ToCallSettings(User u) => new()
    {
        DoNotDisturb = u.DoNotDisturb,
        ForwardAlways = u.ForwardAlways,
        ForwardBusy = u.ForwardBusy,
        ForwardNoAnswer = u.ForwardNoAnswer,
        NoAnswerSeconds = u.NoAnswerSeconds,
        MailboxOnNoAnswer = u.MailboxOnNoAnswer,
        OutboundNumberId = u.OutboundNumberId,
    };

    /// <summary>The own call settings of a person (or an administrator for them). The number shown has to be one the person may show.</summary>
    public static async Task<SaveResult> SaveCallSettingsAsync(MpDbContext db, Actor actor, long userId, CallSettingsInput input, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var result = new SaveResult();
        foreach (var (field, value) in new[] { (nameof(input.ForwardAlways), input.ForwardAlways), (nameof(input.ForwardBusy), input.ForwardBusy), (nameof(input.ForwardNoAnswer), input.ForwardNoAnswer) })
        {
            if (!string.IsNullOrWhiteSpace(value) && NumberRules.Clean(value).Length < 2)
            {
                result.Add(field, Text.T("Telephony.Forward.Invalid"));
            }
        }

        if (input.OutboundNumberId is { } numberId && (await Lookups.OutboundNumbersAsync(db, actor, userId, ct)).All(c => c.Id != numberId))
        {
            result.Add(nameof(input.OutboundNumberId), Text.T("Telephony.Outbound.NotYours"));
        }

        if (!result.Ok)
        {
            return result;
        }

        var user = await db.User.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        user.DoNotDisturb = input.DoNotDisturb;
        user.ForwardAlways = CleanNumber(input.ForwardAlways);
        user.ForwardBusy = CleanNumber(input.ForwardBusy);
        user.ForwardNoAnswer = CleanNumber(input.ForwardNoAnswer);
        user.NoAnswerSeconds = input.NoAnswerSeconds;
        user.MailboxOnNoAnswer = input.MailboxOnNoAnswer;
        user.OutboundNumberId = input.OutboundNumberId;
        await db.SaveChangesAsync(ct);
        TelephonySignals.NotifyUserState(userId);
        return SaveResult.Saved(userId);
    }

    /// <summary>"Nicht stören" on or off (null: the other way round); the function key and the feature code use it. Returns the new state.</summary>
    public static async Task<bool> ToggleDoNotDisturbAsync(MpDbContext db, Actor actor, long userId, bool? on = null, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var user = await db.User.FirstAsync(u => u.Id == userId, ct);
        user.DoNotDisturb = on ?? !user.DoNotDisturb;
        await db.SaveChangesAsync(ct);
        TelephonySignals.NotifyUserState(userId);
        return user.DoNotDisturb;
    }

    /// <summary>Forwarding of all calls to <paramref name="number"/> on, or off when it is on already for that number (the function key "Umleitung").</summary>
    public static async Task<bool> ToggleForwardAsync(MpDbContext db, Actor actor, long userId, string number, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        var user = await db.User.FirstAsync(u => u.Id == userId, ct);
        var clean = NumberRules.Clean(number);
        user.ForwardAlways = user.ForwardAlways == clean ? null : clean;
        await db.SaveChangesAsync(ct);
        TelephonySignals.NotifyUserState(userId);
        return user.ForwardAlways is not null;
    }

    /// <summary>The colleagues as a list (the page "Kollegen"): everybody who is active; every signed-in person may see them.</summary>
    public static IQueryable<User> Colleagues(MpDbContext db, Actor actor)
    {
        actor.RequireSignedIn();
        return db.User.AsNoTracking().Where(u => u.IsActive);
    }

    public static IQueryable<User> InTeam(IQueryable<User> query, MpDbContext db, long teamId)
    {
        var members = db.TeamMember.Where(m => m.TeamId == teamId).Select(m => m.UserId);
        return query.Where(u => members.Contains(u.Id));
    }

    /// <summary>The colleagues for the panel of the web client: everybody active with the first extension and the teams.</summary>
    public static async Task<List<Colleague>> DirectoryAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireSignedIn();
        var people = await db.User.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.DoNotDisturb, Forwarding = u.ForwardAlways != null }).ToListAsync(ct);
        var extensions = await ExtensionsAsync(db, actor, people.Select(p => p.Id), ct);
        var teams = await db.TeamMember.AsNoTracking().Where(m => m.Team!.UpdateState != UpdateState.Deleted).Select(m => new { m.UserId, m.TeamId }).ToListAsync(ct);
        return [.. people.Select(p => new Colleague(p.Id, p.DisplayName, p.Email, extensions.GetValueOrDefault(p.Id)?.Split(", ")[0],
            [.. teams.Where(t => t.UserId == p.Id).Select(t => t.TeamId)], p.DoNotDisturb, p.Forwarding))];
    }

    private static string? CleanNumber(string? value) => string.IsNullOrWhiteSpace(value) ? null : NumberRules.Clean(value);
}
