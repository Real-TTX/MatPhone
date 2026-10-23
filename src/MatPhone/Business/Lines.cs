using MatPhone.Configuration;
using System.ComponentModel.DataAnnotations;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class LineProfileInput
{
    [Required(ErrorMessage = "Profiles.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    [StringLength(200, ErrorMessage = "Common.Validation.TooLong")]
    public string? Host { get; set; }

    [Range(1, 65535, ErrorMessage = "Common.Validation.Port")]
    public int Port { get; set; } = 5060;

    public SipTransport Transport { get; set; } = SipTransport.Udp;

    public bool Register { get; set; } = true;

    [Range(60, 86400, ErrorMessage = "Lines.Expiry.Range")]
    public int RegisterExpirySeconds { get; set; } = 600;

    public string? OutboundProxy { get; set; }

    public CallerIdMode CallerIdMode { get; set; } = CallerIdMode.PAssertedIdentity;

    public NumberFormat NumberFormat { get; set; } = NumberFormat.E164Plus;

    [StringLength(200, ErrorMessage = "Common.Validation.TooLong")]
    public string? UserNameHint { get; set; }

    public string? Domain { get; set; }

    [StringLength(2000, ErrorMessage = "Common.Validation.TooLong")]
    public string? Notes { get; set; }
}

public sealed class LineInput
{
    [Required(ErrorMessage = "Lines.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    public long? ProfileId { get; set; }

    [Required(ErrorMessage = "Lines.Host.Required")]
    [StringLength(200, ErrorMessage = "Common.Validation.TooLong")]
    public string Host { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "Common.Validation.Port")]
    public int Port { get; set; } = 5060;

    public SipTransport Transport { get; set; } = SipTransport.Udp;

    public string? Domain { get; set; }

    public string? OutboundProxy { get; set; }

    [Required(ErrorMessage = "Lines.UserName.Required")]
    [StringLength(200, ErrorMessage = "Common.Validation.TooLong")]
    public string UserName { get; set; } = "";

    public string? AuthUserName { get; set; }

    /// <summary>Empty: the password stays as it is.</summary>
    public string? Password { get; set; }

    public bool Register { get; set; } = true;

    [Range(60, 86400, ErrorMessage = "Lines.Expiry.Range")]
    public int RegisterExpirySeconds { get; set; } = 600;

    public CallerIdMode CallerIdMode { get; set; } = CallerIdMode.PAssertedIdentity;

    public NumberFormat NumberFormat { get; set; } = NumberFormat.E164Plus;

    public string? DefaultCallerNumber { get; set; }

    [Range(0, 1000, ErrorMessage = "Lines.Channels.Range")]
    public int MaxChannels { get; set; }

    public bool IsEnabled { get; set; } = true;
}

/// <summary>Lines (the accounts at providers) and the provider profiles they start from. Only administrators see or change them.</summary>
public static class Lines
{
    /// <summary>The built-in provider profiles: typical settings, to be checked against the papers of the provider (the notes say so).</summary>
    public static readonly IReadOnlyList<LineProfile> BuiltIn =
    [
        new()
        {
            Name = "Telekom (Anschluss, IP-basiert)", IsBuiltIn = true, Host = "tel.t-online.de", Domain = "tel.t-online.de", Port = 5060, Transport = SipTransport.Udp,
            Register = true, RegisterExpirySeconds = 600, CallerIdMode = CallerIdMode.FromHeader, NumberFormat = NumberFormat.E164Plus,
            UserNameHint = "Rufnummer mit Vorwahl im Format +49301234567; Authentifizierung: E-Mail-Adresse des Telekom-Logins",
            Notes = "Für Einzelrufnummern eines Telekom-Anschlusses (MagentaZuhause, DeutschlandLAN IP Voice/Data). Werte mit den Zugangsdaten der Telekom prüfen.",
        },
        new()
        {
            Name = "Telekom (DeutschlandLAN SIP-Trunk / CompanyFlex)", IsBuiltIn = true, Host = "reg.sip-trunk.telekom.de", Domain = "sip-trunk.telekom.de", Port = 5061,
            Transport = SipTransport.Tls, Register = true, RegisterExpirySeconds = 600, CallerIdMode = CallerIdMode.PAssertedIdentity, NumberFormat = NumberFormat.E164Plus,
            UserNameHint = "Registrierungsname aus dem Telekom-Kundencenter",
            Notes = "Der SIP-Trunk der Telekom verlangt TLS und verschlüsselte Sprache (SRTP). Werte mit dem Auftrag der Telekom prüfen.",
        },
        new()
        {
            Name = "peoplefone", IsBuiltIn = true, Host = "sips.peoplefone.de", Domain = "sips.peoplefone.de", Port = 5060, Transport = SipTransport.Udp,
            Register = true, RegisterExpirySeconds = 600, CallerIdMode = CallerIdMode.PAssertedIdentity, NumberFormat = NumberFormat.E164Plus,
            UserNameHint = "SIP-Benutzername aus dem peoplefone-Portal",
            Notes = "Werte mit den SIP-Zugangsdaten im Kundenportal von peoplefone prüfen.",
        },
        new()
        {
            Name = "Vodafone", IsBuiltIn = true, Host = "", Port = 5060, Transport = SipTransport.Udp, Register = true, RegisterExpirySeconds = 600,
            CallerIdMode = CallerIdMode.FromHeader, NumberFormat = NumberFormat.National,
            UserNameHint = "Rufnummer mit Vorwahl, wie in den Zugangsdaten von Vodafone",
            Notes = "Registrar und Domain stehen in den Telefonie-Zugangsdaten von Vodafone (sie unterscheiden sich nach Anschluss: DSL, Kabel, Business).",
        },
        new()
        {
            Name = "sipgate", IsBuiltIn = true, Host = "sipgate.de", Domain = "sipgate.de", Port = 5060, Transport = SipTransport.Udp, Register = true,
            RegisterExpirySeconds = 600, CallerIdMode = CallerIdMode.PAssertedIdentity, NumberFormat = NumberFormat.E164,
            UserNameHint = "SIP-ID aus der sipgate-App (z. B. 1234567e0)",
            Notes = "Für sipgate team/basic. Für sipgate trunking ist der Registrar sipconnect.sipgate.de.",
        },
        new()
        {
            Name = "easybell", IsBuiltIn = true, Host = "sip.easybell.de", Domain = "sip.easybell.de", Port = 5060, Transport = SipTransport.Udp, Register = true,
            RegisterExpirySeconds = 600, CallerIdMode = CallerIdMode.PAssertedIdentity, NumberFormat = NumberFormat.E164Plus,
            UserNameHint = "SIP-Benutzername aus dem easybell-Kundenportal",
            Notes = "Werte mit den Zugangsdaten im Kundenportal von easybell prüfen.",
        },
        new()
        {
            Name = "SIP (allgemein)", IsBuiltIn = true, Host = "", Port = 5060, Transport = SipTransport.Udp, Register = true, RegisterExpirySeconds = 600,
            CallerIdMode = CallerIdMode.PAssertedIdentity, NumberFormat = NumberFormat.E164Plus,
            Notes = "Ein Anbieter ohne eigenes Profil: alle Werte aus den Unterlagen des Anbieters.",
        },
    ];

    /// <summary>Writes the built-in profiles that are missing (by name) on start; a profile the administrator changed keeps the changes.</summary>
    public static async Task EnsureBuiltInProfilesAsync(MpDbContext db, CancellationToken ct = default)
    {
        var names = await db.LineProfile.IgnoreQueryFilters().Where(p => p.IsBuiltIn).Select(p => p.Name).ToListAsync(ct);
        foreach (var profile in BuiltIn.Where(b => !names.Contains(b.Name)))
        {
            db.LineProfile.Add(new LineProfile
            {
                Name = profile.Name, IsBuiltIn = true, Host = profile.Host, Domain = profile.Domain, Port = profile.Port, Transport = profile.Transport,
                Register = profile.Register, RegisterExpirySeconds = profile.RegisterExpirySeconds, CallerIdMode = profile.CallerIdMode, NumberFormat = profile.NumberFormat,
                UserNameHint = profile.UserNameHint, Notes = profile.Notes, OutboundProxy = profile.OutboundProxy,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public static IQueryable<Line> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.Line.AsNoTracking().Include(l => l.Profile);
    }

    public static IQueryable<Line> Search(IQueryable<Line> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(l => EF.Functions.ILike(l.Name, like) || EF.Functions.ILike(l.Host, like) || EF.Functions.ILike(l.UserName, like));
    }

    public static async Task<Line?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Line.AsNoTracking().Include(l => l.Profile).FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public static async Task<List<PhoneNumber>> NumbersAsync(MpDbContext db, Actor actor, long lineId, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.PhoneNumber.AsNoTracking().Include(n => n.User).Include(n => n.CallGroup).Where(n => n.LineId == lineId).OrderBy(n => n.Number).ToListAsync(ct);
    }

    public static LineInput ToInput(Line l) => new()
    {
        Name = l.Name,
        ProfileId = l.ProfileId,
        Host = l.Host,
        Port = l.Port,
        Transport = l.Transport,
        Domain = l.Domain,
        OutboundProxy = l.OutboundProxy,
        UserName = l.UserName,
        AuthUserName = l.AuthUserName,
        Register = l.Register,
        RegisterExpirySeconds = l.RegisterExpirySeconds,
        CallerIdMode = l.CallerIdMode,
        NumberFormat = l.NumberFormat,
        DefaultCallerNumber = l.DefaultCallerNumber,
        MaxChannels = l.MaxChannels,
        IsEnabled = l.IsEnabled,
    };

    /// <summary>A new line as the profile has it (the form of a new line starts from the chosen profile).</summary>
    public static LineInput FromProfile(LineProfile p) => new()
    {
        Name = p.Name,
        ProfileId = p.Id,
        Host = p.Host,
        Port = p.Port,
        Transport = p.Transport,
        Domain = p.Domain,
        OutboundProxy = p.OutboundProxy,
        Register = p.Register,
        RegisterExpirySeconds = p.RegisterExpirySeconds,
        CallerIdMode = p.CallerIdMode,
        NumberFormat = p.NumberFormat,
    };

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, LineInput input, Core.Telephony.NumberPlan plan, SecretProtector secrets,
        ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        string? caller = null;
        if (!string.IsNullOrWhiteSpace(input.DefaultCallerNumber))
        {
            caller = Core.Telephony.NumberRules.ToE164(input.DefaultCallerNumber, plan);
            if (caller is null)
            {
                result.Add(nameof(LineInput.DefaultCallerNumber), Text.T("Numbers.External.Invalid"));
            }
        }

        if (id is null && string.IsNullOrEmpty(input.Password))
        {
            result.Add(nameof(LineInput.Password), Text.T("Lines.Password.Required"));
        }

        if (input.ProfileId is { } profileId && !await db.LineProfile.AnyAsync(p => p.Id == profileId, ct))
        {
            result.Add(nameof(LineInput.ProfileId), Text.T("Common.NotFound"));
        }

        if (!result.Ok)
        {
            return result;
        }

        var line = id is { } existing ? await db.Line.FirstOrDefaultAsync(l => l.Id == existing, ct) : new Line();
        if (line is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        line.Name = input.Name.Trim();
        line.ProfileId = input.ProfileId;
        line.Host = input.Host.Trim();
        line.Port = input.Port;
        line.Transport = input.Transport;
        line.Domain = Clean(input.Domain);
        line.OutboundProxy = Clean(input.OutboundProxy);
        line.UserName = input.UserName.Trim();
        line.AuthUserName = Clean(input.AuthUserName);
        if (!string.IsNullOrEmpty(input.Password))
        {
            line.PasswordProtected = secrets.Protect(input.Password);
        }

        line.Register = input.Register;
        line.RegisterExpirySeconds = input.RegisterExpirySeconds;
        line.CallerIdMode = input.CallerIdMode;
        line.NumberFormat = input.NumberFormat;
        line.DefaultCallerNumber = caller;
        line.MaxChannels = input.MaxChannels;
        line.IsEnabled = input.IsEnabled;
        if (id is null)
        {
            db.Line.Add(line);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Lines.Log.Created" : "Lines.Log.Changed"), nameof(Line), line.Id, line.Name);
        TelephonySignals.Notify(TelephonyChange.Lines);
        return SaveResult.Saved(line.Id);
    }

    public static async Task<string?> DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var line = await db.Line.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (line is null)
        {
            return null;
        }

        if (await db.Rule.AnyAsync(r => r.LineId == id && r.Action == RuleAction.UseLine, ct))
        {
            return Text.T("Lines.Delete.InUse");
        }

        await db.PhoneNumber.Where(n => n.LineId == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.LineId, (long?)null), ct);
        db.Line.Remove(line);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Lines.Log.Deleted"), nameof(Line), line.Id, line.Name);
        TelephonySignals.Notify(TelephonyChange.Lines);
        return null;
    }

    /// <summary>What the engine saw at the last registration ("200 OK", "403 Forbidden"): written by the engine, so the system acts.</summary>
    public static async Task SetStatusAsync(MpDbContext db, long id, string status, DateTime when, CancellationToken ct = default)
        => await db.Line.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.LastStatus, status).SetProperty(l => l.LastStatusDate, when), ct);

    // ---- profiles ----

    public static IQueryable<LineProfile> Profiles(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.LineProfile.AsNoTracking();
    }

    public static IQueryable<LineProfile> SearchProfiles(IQueryable<LineProfile> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(p => EF.Functions.ILike(p.Name, like) || EF.Functions.ILike(p.Host, like));
    }

    /// <summary>The profiles to start a new line from: the built-in ones first.</summary>
    public static async Task<List<LineProfile>> ProfileListAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.LineProfile.AsNoTracking().OrderByDescending(p => p.IsBuiltIn).ThenBy(p => p.Name).ToListAsync(ct);
    }

    public static async Task<LineProfile?> LoadProfileAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.LineProfile.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public static LineProfileInput ToInput(LineProfile p) => new()
    {
        Name = p.Name,
        Host = p.Host,
        Port = p.Port,
        Transport = p.Transport,
        Register = p.Register,
        RegisterExpirySeconds = p.RegisterExpirySeconds,
        OutboundProxy = p.OutboundProxy,
        CallerIdMode = p.CallerIdMode,
        NumberFormat = p.NumberFormat,
        UserNameHint = p.UserNameHint,
        Domain = p.Domain,
        Notes = p.Notes,
    };

    public static async Task<SaveResult> SaveProfileAsync(MpDbContext db, Actor actor, long? id, LineProfileInput input, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var name = input.Name.Trim();
        if (await db.LineProfile.AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), ct))
        {
            return SaveResult.Fail(nameof(LineProfileInput.Name), Text.T("Profiles.Name.Taken"));
        }

        var profile = id is { } existing ? await db.LineProfile.FirstOrDefaultAsync(p => p.Id == existing, ct) : new LineProfile();
        if (profile is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        // A built-in profile keeps its name: it is how the start finds it (and would write it again).
        if (!profile.IsBuiltIn)
        {
            profile.Name = name;
        }

        profile.Host = input.Host?.Trim() ?? "";
        profile.Port = input.Port;
        profile.Transport = input.Transport;
        profile.Register = input.Register;
        profile.RegisterExpirySeconds = input.RegisterExpirySeconds;
        profile.OutboundProxy = Clean(input.OutboundProxy);
        profile.CallerIdMode = input.CallerIdMode;
        profile.NumberFormat = input.NumberFormat;
        profile.UserNameHint = Clean(input.UserNameHint);
        profile.Domain = Clean(input.Domain);
        profile.Notes = Clean(input.Notes);
        if (id is null)
        {
            db.LineProfile.Add(profile);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Profiles.Log.Created" : "Profiles.Log.Changed"), nameof(LineProfile), profile.Id, profile.Name);
        return SaveResult.Saved(profile.Id);
    }

    public static async Task<string?> DeleteProfileAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var profile = await db.LineProfile.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return null;
        }

        if (profile.IsBuiltIn)
        {
            return Text.T("Profiles.Delete.BuiltIn");
        }

        await db.Line.Where(l => l.ProfileId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ProfileId, (long?)null), ct);
        db.LineProfile.Remove(profile);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Profiles.Log.Deleted"), nameof(LineProfile), profile.Id, profile.Name);
        return null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
