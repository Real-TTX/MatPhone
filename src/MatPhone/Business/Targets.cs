using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

/// <summary>The checks of "where a call goes" that numbers, rules and groups share: the target has to exist, and an external target needs a number.</summary>
public static class Targets
{
    public static async Task CheckTargetAsync(MpDbContext db, TargetKind kind, long? userId, long? groupId, long? mailboxId, string? number, SaveResult result,
        string prefix = "Target", CancellationToken ct = default)
    {
        switch (kind)
        {
            case TargetKind.User when userId is null || !await db.User.AnyAsync(u => u.Id == userId, ct):
                result.Add(prefix + "UserId", Text.T("Targets.User.Required"));
                break;
            case TargetKind.Group when groupId is null || !await db.CallGroup.AnyAsync(g => g.Id == groupId, ct):
                result.Add(prefix + "GroupId", Text.T("Targets.Group.Required"));
                break;
            case TargetKind.Mailbox when mailboxId is null || !await db.Mailbox.AnyAsync(m => m.Id == mailboxId, ct):
                result.Add(prefix + "MailboxId", Text.T("Targets.Mailbox.Required"));
                break;
            case TargetKind.External when NumberRules.Clean(number).Length < 2:
                result.Add(prefix + "Number", Text.T("Targets.Number.Required"));
                break;
        }
    }

    public static async Task CheckFallbackAsync(MpDbContext db, FallbackKind kind, long? userId, long? groupId, long? mailboxId, string? number, long? ownGroupId,
        SaveResult result, CancellationToken ct = default)
    {
        if (kind == FallbackKind.Group && groupId is not null && groupId == ownGroupId)
        {
            result.Add("FallbackGroupId", Text.T("Targets.Group.NotItself"));
            return;
        }

        var target = kind switch
        {
            FallbackKind.User => TargetKind.User,
            FallbackKind.Group => TargetKind.Group,
            FallbackKind.Mailbox => TargetKind.Mailbox,
            FallbackKind.External => TargetKind.External,
            _ => TargetKind.None,
        };
        await CheckTargetAsync(db, target, userId, groupId, mailboxId, number, result, "Fallback", ct);
    }
}
