using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

public sealed class DeviceInput
{
    [Required(ErrorMessage = "Devices.Name.Required")]
    [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
    public string Name { get; set; } = "";

    public DeviceKind Kind { get; set; } = DeviceKind.DeskPhone;

    [StringLength(60, ErrorMessage = "Common.Validation.TooLong")]
    public string? Vendor { get; set; }

    [StringLength(60, ErrorMessage = "Common.Validation.TooLong")]
    public string? Model { get; set; }

    public string? MacAddress { get; set; }

    /// <summary>Empty on a new device: one is made up.</summary>
    public string? SipUser { get; set; }

    /// <summary>Empty: a new device gets a generated one, an existing device keeps its own.</summary>
    public string? SipPassword { get; set; }

    public long? UserId { get; set; }

    public long? KeyTemplateId { get; set; }

    public bool IsEnabled { get; set; } = true;
}

/// <summary>SIP phones: their account for the registration, their user and their keys. Only administrators see or change them.</summary>
public static partial class Devices
{
    public const int MinPasswordLength = 12;

    public static IQueryable<Device> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.Device.AsNoTracking().Include(d => d.User);
    }

    public static IQueryable<Device> Search(IQueryable<Device> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(d => EF.Functions.ILike(d.Name, like) || EF.Functions.ILike(d.SipUser, like) || (d.MacAddress != null && EF.Functions.ILike(d.MacAddress, like))
            || (d.Model != null && EF.Functions.ILike(d.Model, like)) || (d.User != null && EF.Functions.ILike(d.User.DisplayName, like)));
    }

    public static async Task<Device?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Device.AsNoTracking().Include(d => d.User).FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public static DeviceInput ToInput(Device d) => new()
    {
        Name = d.Name,
        Kind = d.Kind,
        Vendor = d.Vendor,
        Model = d.Model,
        MacAddress = d.MacAddress,
        SipUser = d.SipUser,
        UserId = d.UserId,
        KeyTemplateId = d.KeyTemplateId,
        IsEnabled = d.IsEnabled,
    };

    /// <summary>The password of the device in plain text, for the administrator who sets up the phone.</summary>
    public static string Password(Actor actor, Device device, SecretProtector secrets)
    {
        actor.RequireAdmin();
        return secrets.Unprotect(device.SipPasswordProtected);
    }

    public static async Task<SaveResult> SaveAsync(MpDbContext db, Actor actor, long? id, DeviceInput input, SecretProtector secrets, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var result = new SaveResult();
        var sipUser = string.IsNullOrWhiteSpace(input.SipUser) ? await NewSipUserAsync(db, ct) : input.SipUser.Trim().ToLowerInvariant();
        if (!SipUserPattern().IsMatch(sipUser))
        {
            result.Add(nameof(DeviceInput.SipUser), Text.T("Devices.SipUser.Invalid"));
        }
        else if (await db.Device.AnyAsync(d => d.Id != id && d.SipUser == sipUser, ct))
        {
            result.Add(nameof(DeviceInput.SipUser), Text.T("Devices.SipUser.Taken"));
        }

        var mac = NormalizeMac(input.MacAddress);
        if (!string.IsNullOrWhiteSpace(input.MacAddress) && mac is null)
        {
            result.Add(nameof(DeviceInput.MacAddress), Text.T("Devices.Mac.Invalid"));
        }

        if (!string.IsNullOrEmpty(input.SipPassword) && input.SipPassword.Length < MinPasswordLength)
        {
            result.Add(nameof(DeviceInput.SipPassword), Text.T("Devices.Password.TooShort", MinPasswordLength));
        }

        if (input.UserId is { } userId && !await db.User.AnyAsync(u => u.Id == userId, ct))
        {
            result.Add(nameof(DeviceInput.UserId), Text.T("Common.NotFound"));
        }

        if (!result.Ok)
        {
            return result;
        }

        var device = id is { } existing ? await db.Device.FirstOrDefaultAsync(d => d.Id == existing, ct) : new Device();
        if (device is null)
        {
            return SaveResult.Fail("", Text.T("Common.NotFound"));
        }

        device.Name = input.Name.Trim();
        device.Kind = input.Kind;
        device.Vendor = Clean(input.Vendor);
        device.Model = Clean(input.Model);
        device.MacAddress = mac;
        device.SipUser = sipUser;
        device.UserId = input.UserId;
        device.KeyTemplateId = input.KeyTemplateId;
        device.IsEnabled = input.IsEnabled;
        if (!string.IsNullOrEmpty(input.SipPassword))
        {
            device.SipPasswordProtected = secrets.Protect(input.SipPassword);
        }
        else if (id is null)
        {
            device.SipPasswordProtected = secrets.Protect(GeneratePassword());
        }

        if (id is null)
        {
            db.Device.Add(device);
        }

        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T(id is null ? "Devices.Log.Created" : "Devices.Log.Changed"), nameof(Device), device.Id, $"{device.Name} ({device.SipUser})");
        TelephonySignals.Notify(TelephonyChange.Devices);
        return SaveResult.Saved(device.Id);
    }

    /// <summary>A new password for the device; the phone has to be given it before it can register again.</summary>
    public static async Task<string?> RenewPasswordAsync(MpDbContext db, Actor actor, long id, SecretProtector secrets, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var device = await db.Device.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return null;
        }

        var password = GeneratePassword();
        device.SipPasswordProtected = secrets.Protect(password);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Devices.Log.Password"), nameof(Device), device.Id, device.Name);
        TelephonySignals.Notify(TelephonyChange.Devices);
        return password;
    }

    public static async Task DeleteAsync(MpDbContext db, Actor actor, long id, ActivityLogger log, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        var device = await db.Device.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return;
        }

        db.Device.Remove(device);
        await db.SaveChangesAsync(ct);
        await log.LogAsync(Text.T("Devices.Log.Deleted"), nameof(Device), device.Id, device.Name);
        TelephonySignals.Notify(TelephonyChange.Devices);
    }

    /// <summary>Letters and digits without the ones that look alike (no 0/O, 1/l/I): it is typed into a phone by hand.</summary>
    public static string GeneratePassword(int length = 16)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }

    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        var hex = new string([.. mac.Where(Uri.IsHexDigit)]).ToLowerInvariant();
        return hex.Length == 12 && mac.Count(c => !Uri.IsHexDigit(c) && c is not (':' or '-' or '.' or ' ')) == 0 ? hex : null;
    }

    /// <summary>"aa:bb:cc:dd:ee:ff" for people.</summary>
    public static string FormatMac(string? mac)
        => mac is { Length: 12 } ? string.Join(':', Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))) : mac ?? string.Empty;

    private static async Task<string> NewSipUserAsync(MpDbContext db, CancellationToken ct)
    {
        while (true)
        {
            var candidate = "phone" + RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!await db.Device.AnyAsync(d => d.SipUser == candidate, ct))
            {
                return candidate;
            }
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,31}$")]
    private static partial Regex SipUserPattern();
}
