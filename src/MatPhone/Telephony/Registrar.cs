using System.Collections.Concurrent;
using MatPhone.Data;
using MatPhone.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace MatPhone.Telephony;

/// <summary>Where a registered phone is: the contact it named and the socket it registered from (behind NAT the only one that reaches it).</summary>
public sealed record Binding(long DeviceId, long? UserId, string DeviceName, SIPURI Contact, SIPEndPoint RemoteEndPoint, DateTime Expires, string? UserAgent);

/// <summary>The account of a device as SIPSorcery checks the digest against it.</summary>
public sealed class DeviceAccount(DeviceInfo device, string realm) : ISIPAccount
{
    public string ID => device.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string SIPUsername => device.SipUser;

    public string SIPPassword => device.Password;

    public string? HA1Digest => null;

    public string SIPDomain => realm;

    public bool IsDisabled => string.IsNullOrEmpty(device.Password);
}

/// <summary>
/// The registrar: phones register with their SIP user name and password (digest), and the registrar keeps where each one is. It also guards the door: an
/// address that keeps failing the authentication is ignored for an hour (SIP scanners try thousands of names).
/// </summary>
public sealed class Registrar(SIPTransport transport, TelephonyLogWriter log, IServiceScopeFactory scopes, TimeProvider clock)
{
    public const string Realm = "matphone";

    private readonly ConcurrentDictionary<long, Binding> _bindings = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _failures = new();
    private readonly ConcurrentDictionary<string, DateTime> _blocked = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastWritten = new();

    public event Action<long?>? Changed;

    public int MaxFailures { get; set; } = 10;

    public int MaxExpiry { get; set; } = 600;

    public IReadOnlyCollection<Binding> Bindings => [.. _bindings.Values.Where(b => b.Expires > clock.GetUtcNow().UtcDateTime)];

    public IEnumerable<Binding> ForUser(long userId) => Bindings.Where(b => b.UserId == userId);

    public bool IsBlocked(SIPEndPoint remote)
        => _blocked.TryGetValue(remote.Address.ToString(), out var until) && until > clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Checks the digest of a request (REGISTER, INVITE, SUBSCRIBE) against the device named in From. Returns the device, or null after answering the request
    /// itself (a challenge, or 403 for a wrong password).
    /// </summary>
    public async Task<DeviceInfo?> AuthenticateAsync(PbxSnapshot snapshot, SIPEndPoint local, SIPEndPoint remote, SIPRequest request)
    {
        var user = request.Header.From?.FromURI?.User ?? "";
        var authorisation = request.Header.AuthenticationHeaders.FirstOrDefault();
        if (authorisation?.SIPDigest?.Username is { Length: > 0 } digestUser)
        {
            user = digestUser;
        }

        snapshot.DevicesBySipUser.TryGetValue(user, out var device);
        // An unknown name gets a challenge like a known one: the answer must not tell a scanner which names exist.
        var account = new DeviceAccount(device ?? new DeviceInfo(0, "", user, Guid.NewGuid().ToString("N"), null), Realm);
        var result = SIPRequestAuthenticator.AuthenticateSIPRequest(local, remote, request, account);
        if (result.Authenticated && device is not null)
        {
            _failures.TryRemove(remote.Address.ToString(), out _);
            return device;
        }

        if (result.AuthenticationRequiredHeader is not null && authorisation is null)
        {
            var challenge = SIPResponse.GetResponse(request, result.ErrorResponse, null);
            challenge.Header.AuthenticationHeaders.Add(result.AuthenticationRequiredHeader);
            await transport.SendResponseAsync(challenge).ConfigureAwait(false);
            return null;
        }

        CountFailure(remote, user, request.Method);
        if (result.AuthenticationRequiredHeader is not null && result.ErrorResponse != SIPResponseStatusCodesEnum.Forbidden)
        {
            // A stale nonce or the next try of a phone: challenge again.
            var again = SIPResponse.GetResponse(request, result.ErrorResponse, null);
            again.Header.AuthenticationHeaders.Add(result.AuthenticationRequiredHeader);
            await transport.SendResponseAsync(again).ConfigureAwait(false);
        }
        else
        {
            await transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Forbidden, null)).ConfigureAwait(false);
        }

        return null;
    }

    private void CountFailure(SIPEndPoint remote, string user, SIPMethodsEnum method)
    {
        var address = remote.Address.ToString();
        var now = clock.GetUtcNow().UtcDateTime;
        var entry = _failures.AddOrUpdate(address, (1, now), (_, old) => now - old.Since > TimeSpan.FromMinutes(10) ? (1, now) : (old.Count + 1, old.Since));
        if (entry.Count == 1 || entry.Count % 5 == 0)
        {
            log.Warning(LogCategory.Security, $"Authentication failed for \"{user}\" ({method})", $"{entry.Count} failures in 10 minutes", address);
        }

        if (entry.Count >= MaxFailures && !_blocked.ContainsKey(address))
        {
            _blocked[address] = now.AddHours(1);
            log.Warning(LogCategory.Security, $"The address {address} is ignored for an hour after {entry.Count} failed authentications", null, address);
        }
    }

    /// <summary>Handles an authenticated REGISTER: adds, refreshes or removes the binding of the device and answers.</summary>
    public async Task RegisterAsync(DeviceInfo device, SIPRequest request, SIPEndPoint remote)
    {
        var contact = request.Header.Contact.FirstOrDefault();
        var requested = contact is { Expires: >= 0 } c ? c.Expires : request.Header.Expires >= 0 ? request.Header.Expires : 3600;
        var expiry = (int)Math.Min(requested, MaxExpiry);
        var now = clock.GetUtcNow().UtcDateTime;
        var response = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null);

        if (contact is null)
        {
            // A query of the bindings: answer with the one there is.
            if (_bindings.TryGetValue(device.Id, out var current))
            {
                response.Header.Contact = [new SIPContactHeader(null, current.Contact) { Expires = (long)Math.Max(0, (current.Expires - now).TotalSeconds) }];
            }

            await transport.SendResponseAsync(response).ConfigureAwait(false);
            return;
        }

        if (expiry == 0 || contact.ContactURI.ToString() == "*")
        {
            if (_bindings.TryRemove(device.Id, out _))
            {
                log.Info(LogCategory.Registration, $"{device.Name} signed off", null, remote.ToString(), deviceId: device.Id, userId: device.UserId);
                Changed?.Invoke(device.UserId);
            }

            await transport.SendResponseAsync(response).ConfigureAwait(false);
            return;
        }

        var isNew = !_bindings.TryGetValue(device.Id, out var previous) || previous.Expires <= now || !previous.RemoteEndPoint.Equals(remote);
        var binding = new Binding(device.Id, device.UserId, device.Name, contact.ContactURI.CopyOf(), remote.CopyOf(), now.AddSeconds(expiry), request.Header.UserAgent);
        _bindings[device.Id] = binding;
        var granted = new SIPContactHeader(null, contact.ContactURI) { Expires = expiry };
        response.Header.Contact = [granted];
        response.Header.Expires = expiry;
        await transport.SendResponseAsync(response).ConfigureAwait(false);

        if (isNew)
        {
            log.Info(LogCategory.Registration, $"{device.Name} registered", request.Header.UserAgent, remote.ToString(), deviceId: device.Id, userId: device.UserId);
            Changed?.Invoke(device.UserId);
        }

        await RememberAsync(binding, isNew).ConfigureAwait(false);
    }

    /// <summary>Removes the bindings of devices that are no longer there (deleted, disabled) and those that ran out; tells who changed.</summary>
    public void Prune(PbxSnapshot snapshot)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var binding in _bindings.Values)
        {
            var known = snapshot.DevicesBySipUser.Values.Any(d => d.Id == binding.DeviceId);
            if ((!known || binding.Expires <= now) && _bindings.TryRemove(binding.DeviceId, out _))
            {
                if (binding.Expires <= now)
                {
                    log.Info(LogCategory.Registration, $"The registration of {binding.DeviceName} ran out", null, binding.RemoteEndPoint.ToString(), deviceId: binding.DeviceId, userId: binding.UserId);
                }

                Changed?.Invoke(binding.UserId);
            }
        }
    }

    /// <summary>When the device registered last, from where and as what (the device page shows it). Written at most every ten minutes per device.</summary>
    private async Task RememberAsync(Binding binding, bool force)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!force && _lastWritten.TryGetValue(binding.DeviceId, out var last) && now - last < TimeSpan.FromMinutes(10))
        {
            return;
        }

        _lastWritten[binding.DeviceId] = now;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            var agent = binding.UserAgent is { Length: > 200 } ua ? ua[..200] : binding.UserAgent;
            await db.Device.Where(d => d.Id == binding.DeviceId).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LastRegisterDate, now)
                .SetProperty(d => d.LastUserAgent, agent)
                .SetProperty(d => d.LastAddress, binding.RemoteEndPoint.ToString())).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warning(LogCategory.Registration, "The registration could not be saved", ex.Message);
        }
    }
}
