using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace MatPhone.Telephony;

/// <summary>How a line stands right now.</summary>
public sealed record LineStatus(long LineId, string Name, bool Registered, string Status, DateTime? Since, int Calls);

/// <summary>
/// The lines to the providers: registers each one that should register (and again when it changes), knows the addresses of the providers so that an INVITE from
/// one of them is accepted without a password, and builds the SIP target of a call out.
/// </summary>
public sealed class LineManager(SIPTransport transport, TelephonyLogWriter log, IServiceScopeFactory scopes, TimeProvider clock)
{
    private readonly ConcurrentDictionary<long, Registration> _registrations = new();
    private readonly ConcurrentDictionary<long, string> _status = new();
    private readonly ConcurrentDictionary<long, DateTime> _since = new();
    private volatile IReadOnlyDictionary<IPAddress, long> _addresses = new Dictionary<IPAddress, long>();

    private sealed record Registration(LineInfo Line, SIPRegistrationUserAgent Agent, string ContactUser);

    /// <summary>The user part of the Contact of each line's registration: a token that only the provider knows, so its INVITE names the line.</summary>
    public long? LineOfContactUser(string? user)
        => user is null ? null : _registrations.Values.FirstOrDefault(r => r.ContactUser == user)?.Line.Id;

    public long? LineOfAddress(IPAddress address) => _addresses.TryGetValue(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, out var id) ? id : null;

    public IReadOnlyList<LineStatus> Status(PbxSnapshot snapshot, Func<long, int> callsOf)
        => [.. snapshot.Lines.Select(l => new LineStatus(l.Id, l.Name, _registrations.TryGetValue(l.Id, out var r) && r.Agent.IsRegistered,
            _status.GetValueOrDefault(l.Id) ?? (l.Register ? "…" : "–"), _since.TryGetValue(l.Id, out var since) ? since : null, callsOf(l.Id)))];

    /// <summary>Brings the registrations in line with the configuration: new and changed lines register (again), removed ones sign off.</summary>
    public async Task ApplyAsync(PbxSnapshot snapshot, IPAddress? publicAddress, int sipPort)
    {
        foreach (var (id, registration) in _registrations)
        {
            var now = snapshot.Line(id);
            if (now is null || !now.Register || now != registration.Line)
            {
                registration.Agent.Stop();
                _registrations.TryRemove(id, out _);
                _status.TryRemove(id, out _);
            }
        }

        foreach (var line in snapshot.Lines.Where(l => l.Register && !string.IsNullOrWhiteSpace(l.Host)))
        {
            if (_registrations.ContainsKey(line.Id))
            {
                continue;
            }

            var contactUser = "mp" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            var aor = SIPURI.ParseSIPURIRelaxed($"{line.UserName}@{line.SipDomain}");
            var contactHost = publicAddress is null ? IPAddress.Any.ToString() : publicAddress.ToString();
            var contact = SIPURI.ParseSIPURI($"sip:{contactUser}@{contactHost}:{sipPort}");
            var registrar = Registrar(line);
            var agent = new SIPRegistrationUserAgent(transport, OutboundProxy(line), aor, line.AuthUserName ?? line.UserName, line.Password, null, registrar, contact,
                line.Expiry, null, maxRegistrationAttemptTimeout: 60, registerFailureRetryInterval: 120, maxRegisterAttempts: int.MaxValue, exitOnUnequivocalFailure: false)
            {
                UserAgent = "MatPhone",
            };
            var lineId = line.Id;
            agent.RegistrationSuccessful += (_, response) => SetStatus(lineId, line.Name, $"{response?.StatusCode} {response?.ReasonPhrase}".Trim(), true);
            agent.RegistrationFailed += (_, response, error) => SetStatus(lineId, line.Name, response is null ? error : $"{response.StatusCode} {response.ReasonPhrase}", false, error);
            agent.RegistrationTemporaryFailure += (_, response, error) => SetStatus(lineId, line.Name, response is null ? error : $"{response.StatusCode} {response.ReasonPhrase}", false, error);
            _registrations[line.Id] = new Registration(line, agent, contactUser);
            agent.Start();
        }

        await ResolveAddressesAsync(snapshot).ConfigureAwait(false);
    }

    public void StopAll()
    {
        foreach (var registration in _registrations.Values)
        {
            registration.Agent.Stop();
        }

        _registrations.Clear();
    }

    /// <summary>The target of a call out over a line: the request URI in the format of the provider, the number shown in the header the provider reads.</summary>
    public SipTarget TargetOf(RouteExternal route, CallerId caller, Core.Telephony.NumberPlan plan)
    {
        var line = route.Line;
        var host = line.Port is 5060 or 0 || line.Transport == SipTransport.Tls ? line.SipDomain : $"{line.SipDomain}:{line.Port}";
        var transportParam = line.Transport switch { SipTransport.Tcp => ";transport=tcp", SipTransport.Tls => ";transport=tls", _ => string.Empty };
        var scheme = line.Transport == SipTransport.Tls ? "sips" : "sip";
        var uri = $"{scheme}:{route.RequestNumber}@{host}{transportParam}";
        var shown = route.CallerNumber is null ? null : Core.Telephony.NumberRules.Format(route.CallerNumber, (Core.Telephony.NumberFormatKind)(int)line.NumberFormat, plan);
        var fromUser = line.CallerIdMode == CallerIdMode.FromHeader && shown is not null ? shown : line.UserName;
        var headers = new List<string>();
        if (shown is not null)
        {
            switch (line.CallerIdMode)
            {
                case CallerIdMode.PAssertedIdentity:
                    headers.Add($"P-Asserted-Identity: <sip:{shown}@{line.SipDomain}>");
                    break;
                case CallerIdMode.PPreferredIdentity:
                    headers.Add($"P-Preferred-Identity: <sip:{shown}@{line.SipDomain}>");
                    break;
            }
        }

        var name = string.IsNullOrWhiteSpace(caller.Name) ? string.Empty : $"\"{caller.Name.Replace("\"", string.Empty, StringComparison.Ordinal)}\" ";
        return new SipTarget(uri, OutboundProxy(line), $"{name}<sip:{fromUser}@{line.SipDomain}>", $"<{scheme}:{route.RequestNumber}@{line.SipDomain}>",
            line.UserName, line.AuthUserName ?? line.UserName, line.Password, headers);
    }

    private static string Registrar(LineInfo line)
    {
        var host = line.Port is 5060 or 0 || line.Transport == SipTransport.Tls ? line.Host : $"{line.Host}:{line.Port}";
        return line.Transport switch
        {
            SipTransport.Tcp => $"{host};transport=tcp",
            SipTransport.Tls => $"sips:{host}",
            _ => host,
        };
    }

    private static SIPEndPoint? OutboundProxy(LineInfo line)
    {
        if (string.IsNullOrWhiteSpace(line.OutboundProxy))
        {
            return null;
        }

        var text = line.OutboundProxy.Trim();
        if (SIPEndPoint.TryParse(text) is { } parsed)
        {
            return parsed;
        }

        // A host name: resolved once here (a proxy that moves needs the line saved again).
        var (host, port) = text.Contains(':') && int.TryParse(text[(text.LastIndexOf(':') + 1)..], out var p) ? (text[..text.LastIndexOf(':')], p) : (text, 5060);
        var address = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        var protocol = line.Transport switch { SipTransport.Tcp => SIPProtocolsEnum.tcp, SipTransport.Tls => SIPProtocolsEnum.tls, _ => SIPProtocolsEnum.udp };
        return address is null ? null : new SIPEndPoint(protocol, address, port);
    }

    private void SetStatus(long lineId, string name, string status, bool registered, string? error = null)
    {
        var previous = _status.GetValueOrDefault(lineId);
        _status[lineId] = status;
        if (previous == status)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        _since[lineId] = now;
        if (registered)
        {
            log.Info(LogCategory.Line, $"The line {name} is registered ({status})", null, lineId: lineId);
        }
        else
        {
            log.Warning(LogCategory.Line, $"The line {name} could not register: {status}", error, lineId: lineId);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await Lines.SetStatusAsync(scope.ServiceProvider.GetRequiredService<MpDbContext>(), lineId, status.Length > 100 ? status[..100] : status, now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Warning(LogCategory.Line, "The status of a line could not be saved", ex.Message, lineId: lineId);
            }
        });
    }

    /// <summary>The addresses of the providers (host, domain, proxy), so that their INVITEs are recognised.</summary>
    private async Task ResolveAddressesAsync(PbxSnapshot snapshot)
    {
        var map = new Dictionary<IPAddress, long>();
        foreach (var line in snapshot.Lines)
        {
            foreach (var name in new[] { line.Host, line.Domain, line.OutboundProxy?.Split(':')[0] }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            {
                try
                {
                    foreach (var address in await Dns.GetHostAddressesAsync(name!).ConfigureAwait(false))
                    {
                        map.TryAdd(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, line.Id);
                    }
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    log.Warning(LogCategory.Line, $"The address of {name} ({line.Name}) could not be resolved", ex.Message, lineId: line.Id);
                }
            }
        }

        _addresses = map;
    }
}
