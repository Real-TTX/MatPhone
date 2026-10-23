using System.ComponentModel.DataAnnotations;
using MatPhone.Core.Audio;
using MatPhone.Core.Security;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using MatPhone.Storage;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class MailboxInput
{
    [Required(ErrorMessage = "Mailboxes.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    /// <summary>"user", "group" or "none": whose mailbox it is.</summary>
    public string Owner { get; set; } = "user";

    public long? UserId { get; set; }

    public long? CallGroupId { get; set; }

    /// <summary>Empty: the PIN stays as it is (a new mailbox has none: listening from a phone is then only possible for its owner's own devices).</summary>
    public string? Pin { get; set; }

    [Range(10, 600, ErrorMessage = "Mailboxes.MaxSeconds.Range")]
    public int MaxMessageSeconds { get; set; } = 120;

    public bool MailNotify { get; set; }

    [EmailAddress(ErrorMessage = "Common.Validation.Email")]
    public string? MailAddress { get; set; }

    public bool MailAttachAudio { get; set; } = true;

    public bool IsActive { get; set; } = true;
}

/// <summary>A message as a list shows it, with the mailbox it is in.</summary>
public sealed record MessageView
{
    public long Id { get; init; }

    public long MailboxId { get; init; }

    public string MailboxName { get; init; } = "";

    public string? CallerNumber { get; init; }

    public string? CallerName { get; init; }

    public DateTime ReceivedDate { get; init; }

    public int DurationSeconds { get; init; }

    public bool IsNew { get; init; }

    public bool HasAudio { get; init; }
}

/// <summary>
/// Mailboxes and their messages. Administrators set them up; a person reads and deletes the messages of the own mailbox and of the mailboxes of the own groups,
/// and records the own greeting.
/// </summary>
public static class Mailboxes
{
    public const int MaxGreetingSeconds = 60;

    public static IQueryable<Mailbox> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.Mailbox.AsNoTracking().Include(m => m.User).Include(m => m.CallGroup);
    }

    public static IQueryable<Mailbox> Search(IQueryable<Mailbox> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(m => EF.Functions.ILike(m.Name, like) || (m.User != null && EF.Functions.ILike(m.User.DisplayName, like)) || (m.CallGroup != null && EF.Functions.ILike(m.CallGroup.Name, like)));
    }

    public static async Task<Mailbox?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Mailbox.AsNoTracking().Include(m => m.User).Include(m => m.CallGroup).FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public static MailboxInput ToInput(Mailbox m) => new()
    {
        Name = m.Name,
        Owner = m.UserId is not null ? "user" : m.CallGroupId is not null ? "group" : "none",
        UserId = m.UserId,
        CallGroupId = m.CallGroupId,
        MaxMessageSeconds = m.MaxMessageSeconds,
        MailNotify = m.MailNotify,
        MailAddress = m.MailAddress,
        MailAttachAudio = m.MailAttachAudio,
        IsActive = m.IsActive,
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, MailboxInput input, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        if (input.Owner == "user" && (input.UserId is null || !await db.User.AnyAsync(u => u.Id == input.UserId, ct)))
        {
            result.Add(nameof(MailboxInput.UserId), Text.T("Targets.User.Required"));
        }

        if (input.Owner == "group" && (input.CallGroupId is null || !await db.CallGroup.AnyAsync(g => g.Id == input.CallGroupId, ct)))
        {
            result.Add(nameof(MailboxInput.CallGroupId), Text.T("Targets.Group.Required"));
        }

        if (input.Owner == "user" && input.UserId is { } owner && await db.Mailbox.AnyAsync(m => m.Id != id && m.UserId == owner, ct))
        {
            result.Add(nameof(MailboxInput.UserId), Text.T("Mailboxes.User.HasOne"));
        }

        if (CheckPin(input.Pin) is { } pinProblem)
        {
            result.Add(nameof(MailboxInput.Pin), pinProblem);
        }

        if (input.MailNotify && string.IsNullOrWhiteSpace(input.MailAddress) && input.Owner != "user")
        {
            result.Add(nameof(MailboxInput.MailAddress), Text.T("Mailboxes.MailAddress.Required"));
        }

        if (!result.Ok)
        {
            return result;
        }

        var box = id is { } existing ? await db.Mailbox.FirstOrDefaultAsync(m => m.Id == existing, ct) : new Mailbox();
        if (box is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        box.Name = input.Name.Trim();
        box.UserId = input.Owner == "user" ? input.UserId : null;
        box.CallGroupId = input.Owner == "group" ? input.CallGroupId : null;
        if (!string.IsNullOrEmpty(input.Pin))
        {
            box.PinHash = PasswordHasher.Hash(input.Pin);
        }

        box.MaxMessageSeconds = input.MaxMessageSeconds;
        box.MailNotify = input.MailNotify;
        box.MailAddress = string.IsNullOrWhiteSpace(input.MailAddress) ? null : input.MailAddress.Trim();
        box.MailAttachAudio = input.MailAttachAudio;
        box.IsActive = input.IsActive;
        if (id is null)
        {
            db.Mailbox.Add(box);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Mailboxes.Log.Created" : "Mailboxes.Log.Changed"), nameof(Mailbox), box.Id, box.Name);
        TelephonySignals.Notify(TelephonyChange.Mailboxes);
        return SaveResult.Saved(box.Id);
    }

    public static async Task<string?> DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var box = await db.Mailbox.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (box is null)
        {
            return null;
        }

        if (await db.PhoneNumber.AnyAsync(n => n.MailboxId == id && n.Target == TargetKind.Mailbox, ct)
            || await db.Rule.AnyAsync(r => r.TargetMailboxId == id && r.Target == TargetKind.Mailbox, ct)
            || await db.CallGroup.AnyAsync(g => g.FallbackMailboxId == id && g.Fallback == FallbackKind.Mailbox, ct))
        {
            return Text.T("Mailboxes.Delete.InUse");
        }

        db.Mailbox.Remove(box);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Mailboxes.Log.Deleted"), nameof(Mailbox), box.Id, box.Name);
        TelephonySignals.Notify(TelephonyChange.Mailboxes);
        return null;
    }

    /// <summary>Null when the PIN is fine (empty, or 4 to 8 digits).</summary>
    public static string? CheckPin(string? pin)
        => string.IsNullOrEmpty(pin) || (pin.Length is >= 4 and <= 8 && pin.All(char.IsAsciiDigit)) ? null : Text.T("Mailboxes.Pin.Invalid");

    /// <summary>
    /// The mailboxes a person may listen to: the own one and those of the person's groups. An administrator also hears the mailboxes of nobody
    /// (but not those of other people); the system (no person) all of them.
    /// </summary>
    public static IQueryable<Mailbox> Accessible(MpDbContext db, Actor actor)
    {
        actor.RequireSignedIn();
        if (actor.IsAdmin && actor.UserId is null)
        {
            return db.Mailbox;
        }

        var me = actor.Me;
        var admin = actor.IsAdmin;
        var groups = db.CallGroupMember.Where(m => m.UserId == me).Select(m => m.CallGroupId);
        return db.Mailbox.Where(m => m.UserId == me || (m.CallGroupId != null && groups.Contains(m.CallGroupId.Value))
            || (admin && m.UserId == null && m.CallGroupId == null));
    }

    /// <summary>The own mailbox of a person (or null).</summary>
    public static async Task<Mailbox?> OwnAsync(MpDbContext db, Actor actor, long userId, CancellationToken ct = default)
    {
        actor.RequireSelfOrAdmin(userId);
        return await db.Mailbox.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId, ct);
    }

    public static IQueryable<MessageView> Messages(MpDbContext db, Actor actor)
    {
        var boxes = Accessible(db, actor).Select(m => m.Id);
        return db.MailboxMessage.AsNoTracking().Where(m => boxes.Contains(m.MailboxId))
            .Select(m => new MessageView
            {
                Id = m.Id,
                MailboxId = m.MailboxId,
                MailboxName = m.Mailbox!.Name,
                CallerNumber = m.CallerNumber,
                CallerName = m.CallerName,
                ReceivedDate = m.ReceivedDate,
                DurationSeconds = m.DurationSeconds,
                IsNew = m.IsNew,
                HasAudio = m.AudioFileId != null,
            });
    }

    public static IQueryable<MessageView> SearchMessages(IQueryable<MessageView> query, string text)
    {
        var like = Like.Contains(text);
        var digits = Core.Telephony.NumberRules.Clean(text).TrimStart('+', '0');
        var digitsLike = digits.Length > 0 ? Like.Contains(digits) : like;
        return query.Where(m => (m.CallerNumber != null && EF.Functions.ILike(m.CallerNumber, digitsLike)) || (m.CallerName != null && EF.Functions.ILike(m.CallerName, like))
            || EF.Functions.ILike(m.MailboxName, like));
    }

    /// <summary>The messages not listened to yet, of the own mailbox and the group mailboxes (the badge of the shell).</summary>
    public static Task<int> NewCountAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
        => Messages(db, actor).CountAsync(m => m.IsNew, ct);

    /// <summary>The recording of a message (and it counts as heard), or null when it is not there or not the person's.</summary>
    public static async Task<(byte[] Wav, string FileName)?> AudioAsync(MpDbContext db, Actor actor, long messageId, FileStoreService files, CancellationToken ct = default)
    {
        var boxes = Accessible(db, actor).Select(m => m.Id);
        var message = await db.MailboxMessage.Include(m => m.AudioFile).FirstOrDefaultAsync(m => m.Id == messageId && boxes.Contains(m.MailboxId), ct);
        if (message?.AudioFile is not { } file)
        {
            return null;
        }

        if (message.IsNew)
        {
            message.IsNew = false;
            await db.SaveChangesAsync(ct);
            if (actor.UserId is { } me)
            {
                TelephonySignals.NotifyUserState(me);
            }
        }

        return (await files.ReadAllAsync(file, ct), file.FileName);
    }

    public static async Task<bool> MarkAsync(MpDbContext db, Actor actor, long messageId, bool isNew, CancellationToken ct = default)
    {
        var boxes = Accessible(db, actor).Select(m => m.Id);
        var count = await db.MailboxMessage.Where(m => m.Id == messageId && boxes.Contains(m.MailboxId)).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsNew, isNew), ct);
        if (count > 0 && actor.UserId is { } me)
        {
            TelephonySignals.NotifyUserState(me);
        }

        return count > 0;
    }

    public static async Task<bool> DeleteMessageAsync(MpDbContext db, Actor actor, long messageId, CancellationToken ct = default)
    {
        var boxes = Accessible(db, actor).Select(m => m.Id);
        var message = await db.MailboxMessage.FirstOrDefaultAsync(m => m.Id == messageId && boxes.Contains(m.MailboxId), ct);
        if (message is null)
        {
            return false;
        }

        db.MailboxMessage.Remove(message);
        await db.SaveChangesAsync(ct);
        if (actor.UserId is { } me)
        {
            TelephonySignals.NotifyUserState(me);
        }

        return true;
    }

    /// <summary>
    /// A new greeting from a WAV file (any PCM WAV: it is brought to 8 kHz mono), for the owner of the mailbox or an administrator. Null when it was taken,
    /// otherwise what is wrong.
    /// </summary>
    public static async Task<string?> SetGreetingAsync(MpDbContext db, Actor actor, long mailboxId, byte[] wav, FileStoreService files, CancellationToken ct = default)
    {
        var box = await Accessible(db, actor).FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (box is null || (!actor.IsAdmin && box.UserId != actor.UserId))
        {
            throw new AccessDeniedException();
        }

        var samples = Wav.ReadAs8kMono(wav);
        if (samples is null)
        {
            return Text.T("Mailboxes.Greeting.NotWav");
        }

        if (samples.Length > MaxGreetingSeconds * G711.SampleRate)
        {
            return Text.T("Mailboxes.Greeting.TooLong", MaxGreetingSeconds);
        }

        var stored = await files.SaveAsync(Wav.Write(samples), $"greeting-{box.Id}.wav", "audio/wav", ct);
        box.GreetingFile = stored;
        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Mailboxes);
        return null;
    }

    public static async Task RemoveGreetingAsync(MpDbContext db, Actor actor, long mailboxId, CancellationToken ct = default)
    {
        var box = await Accessible(db, actor).FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (box is null || (!actor.IsAdmin && box.UserId != actor.UserId))
        {
            throw new AccessDeniedException();
        }

        box.GreetingFileId = null;
        await db.SaveChangesAsync(ct);
        TelephonySignals.Notify(TelephonyChange.Mailboxes);
    }

    public static async Task<byte[]?> GreetingAsync(MpDbContext db, Actor actor, long mailboxId, FileStoreService files, CancellationToken ct = default)
    {
        var box = await Accessible(db, actor).Include(m => m.GreetingFile).FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        return box?.GreetingFile is { } file ? await files.ReadAllAsync(file, ct) : null;
    }

    /// <summary>The owner of a mailbox sets the own PIN (from the settings of the account).</summary>
    public static async Task<string?> SetPinAsync(MpDbContext db, Actor actor, long mailboxId, string pin, CancellationToken ct = default)
    {
        var box = await Accessible(db, actor).FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (box is null || (!actor.IsAdmin && box.UserId != actor.UserId))
        {
            throw new AccessDeniedException();
        }

        var problem = string.IsNullOrEmpty(pin) ? Text.T("Mailboxes.Pin.Invalid") : CheckPin(pin);
        if (problem is not null)
        {
            return problem;
        }

        box.PinHash = PasswordHasher.Hash(pin);
        await db.SaveChangesAsync(ct);
        return null;
    }
}
