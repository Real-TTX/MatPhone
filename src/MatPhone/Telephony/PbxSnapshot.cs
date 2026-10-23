using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Telephony;

public sealed record UserInfo(long Id, string Name, bool WebClientEnabled, bool DoNotDisturb, string? ForwardAlways, string? ForwardBusy, string? ForwardNoAnswer,
    int NoAnswerSeconds, bool MailboxOnNoAnswer, long? OutboundNumberId, string? Extension, long? MailboxId);

public sealed record DeviceInfo(long Id, string Name, string SipUser, string Password, long? UserId);

public sealed record NumberInfo(long Id, string Number, NumberKind Kind, long? LineId, TargetKind Target, long? UserId, long? GroupId, long? MailboxId, string? TargetNumber);

public sealed record GroupMemberInfo(long UserId, int Position, bool IsLoggedIn, DateTime? LastCallDate);

public sealed record GroupInfo(long Id, string Name, RingStrategy Strategy, int RingSeconds, int MaxWaitSeconds, FallbackKind Fallback, long? FallbackUserId,
    long? FallbackGroupId, long? FallbackMailboxId, string? FallbackNumber, string? CallerNamePrefix, IReadOnlyList<GroupMemberInfo> Members, string? Extension);

public sealed record MailboxInfo(long Id, string Name, long? UserId, long? GroupId, string? PinHash, long? GreetingFileId, int MaxMessageSeconds);

public sealed record LineInfo(long Id, string Name, string Host, int Port, SipTransport Transport, string? Domain, string? OutboundProxy, string UserName,
    string? AuthUserName, string Password, bool Register, int Expiry, CallerIdMode CallerIdMode, NumberFormat NumberFormat, string? DefaultCallerNumber, int MaxChannels)
{
    /// <summary>The SIP domain of the line: the domain when it is set, else the host.</summary>
    public string SipDomain => string.IsNullOrWhiteSpace(Domain) ? Host : Domain;
}

/// <summary>
/// What the engine knows of the configuration, read from the database in one go and replaced as a whole when something changes (the business layer signals it).
/// Immutable: a call that started keeps the snapshot it started with.
/// </summary>
public sealed class PbxSnapshot
{
    public required TelephonyConfig Settings { get; init; }

    public required NumberPlan Plan { get; init; }

    public required IReadOnlyDictionary<long, UserInfo> Users { get; init; }

    public required IReadOnlyDictionary<string, DeviceInfo> DevicesBySipUser { get; init; }

    public required IReadOnlyDictionary<string, NumberInfo> Numbers { get; init; }

    public required IReadOnlyDictionary<long, GroupInfo> Groups { get; init; }

    public required IReadOnlyDictionary<long, MailboxInfo> Mailboxes { get; init; }

    public required IReadOnlyList<LineInfo> Lines { get; init; }

    public required IReadOnlyList<Rule> InboundRules { get; init; }

    public required IReadOnlyList<Rule> OutboundRules { get; init; }

    public static readonly PbxSnapshot Empty = new()
    {
        Settings = new TelephonyConfig(),
        Plan = new NumberPlan(),
        Users = new Dictionary<long, UserInfo>(),
        DevicesBySipUser = new Dictionary<string, DeviceInfo>(),
        Numbers = new Dictionary<string, NumberInfo>(),
        Groups = new Dictionary<long, GroupInfo>(),
        Mailboxes = new Dictionary<long, MailboxInfo>(),
        Lines = [],
        InboundRules = [],
        OutboundRules = [],
    };

    public LineInfo? Line(long? id) => id is null ? null : Lines.FirstOrDefault(l => l.Id == id);

    public static async Task<PbxSnapshot> LoadAsync(MpDbContext db, SecretProtector secrets, TelephonyConfig settings, CancellationToken ct = default)
    {
        var numbers = await db.PhoneNumber.AsNoTracking().Where(n => n.IsActive).ToListAsync(ct);
        var mailboxes = await db.Mailbox.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
        var users = await db.User.AsNoTracking().Where(u => u.IsActive).ToListAsync(ct);
        var extensions = numbers.Where(n => n.Kind == NumberKind.Internal && n.Target == TargetKind.User && n.UserId is not null)
            .GroupBy(n => n.UserId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id).First().Number);
        var groupExtensions = numbers.Where(n => n.Kind == NumberKind.Internal && n.Target == TargetKind.Group && n.CallGroupId is not null)
            .GroupBy(n => n.CallGroupId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id).First().Number);
        var groups = await db.CallGroup.AsNoTracking().Include(g => g.Members).Where(g => g.IsActive).ToListAsync(ct);
        var devices = await db.Device.AsNoTracking().Where(d => d.IsEnabled).ToListAsync(ct);
        var lines = await db.Line.AsNoTracking().Where(l => l.IsEnabled).OrderBy(l => l.Id).ToListAsync(ct);
        var rules = await db.Rule.AsNoTracking().Where(r => r.IsEnabled).OrderBy(r => r.Position).ThenBy(r => r.Id).ToListAsync(ct);

        return new PbxSnapshot
        {
            Settings = settings,
            Plan = new NumberPlan(settings.CountryCode, settings.AreaCode, settings.OutsideLinePrefix),
            Users = users.ToDictionary(u => u.Id, u => new UserInfo(u.Id, u.DisplayName, u.WebClientEnabled, u.DoNotDisturb, u.ForwardAlways, u.ForwardBusy, u.ForwardNoAnswer,
                u.NoAnswerSeconds, u.MailboxOnNoAnswer, u.OutboundNumberId, extensions.GetValueOrDefault(u.Id), mailboxes.FirstOrDefault(m => m.UserId == u.Id)?.Id)),
            DevicesBySipUser = devices.ToDictionary(d => d.SipUser, d => new DeviceInfo(d.Id, d.Name, d.SipUser, secrets.Unprotect(d.SipPasswordProtected), d.UserId),
                StringComparer.OrdinalIgnoreCase),
            Numbers = numbers.ToDictionary(n => n.Number, n => new NumberInfo(n.Id, n.Number, n.Kind, n.LineId, n.Target, n.UserId, n.CallGroupId, n.MailboxId, n.TargetNumber)),
            Groups = groups.ToDictionary(g => g.Id, g => new GroupInfo(g.Id, g.Name, g.Strategy, g.RingSeconds, g.MaxWaitSeconds, g.Fallback, g.FallbackUserId, g.FallbackGroupId,
                g.FallbackMailboxId, g.FallbackNumber, g.CallerNamePrefix,
                [.. g.Members.OrderBy(m => m.Position).Select(m => new GroupMemberInfo(m.UserId, m.Position, m.IsLoggedIn, m.LastCallDate))], groupExtensions.GetValueOrDefault(g.Id))),
            Mailboxes = mailboxes.ToDictionary(m => m.Id, m => new MailboxInfo(m.Id, m.Name, m.UserId, m.CallGroupId, m.PinHash, m.GreetingFileId, m.MaxMessageSeconds)),
            Lines = [.. lines.Select(l => new LineInfo(l.Id, l.Name, l.Host, l.Port, l.Transport, l.Domain, l.OutboundProxy, l.UserName, l.AuthUserName,
                secrets.Unprotect(l.PasswordProtected), l.Register, l.RegisterExpirySeconds, l.CallerIdMode, l.NumberFormat, l.DefaultCallerNumber, l.MaxChannels))],
            InboundRules = [.. rules.Where(r => r.Direction == RuleDirection.Inbound)],
            OutboundRules = [.. rules.Where(r => r.Direction == RuleDirection.Outbound)],
        };
    }
}
