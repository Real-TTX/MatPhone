using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace MatPhone.Telephony;

/// <summary>A registered phone as the status pages show it.</summary>
public sealed record RegistrationView(long DeviceId, string Device, long? UserId, string Address, string? UserAgent, DateTime Expires);

/// <summary>A call as the status pages show it.</summary>
public sealed record LiveCall(string Id, CallDirection Direction, string? From, string? FromName, string? To, string State, DateTime Start, DateTime? Answer);

/// <summary>What the dashboard and the status pages read of the engine.</summary>
public interface IPbxStatus
{
    bool IsRunning { get; }

    /// <summary>Why the engine does not run (the port is taken ...), or a warning (no public address); null when all is well.</summary>
    string? Problem { get; }

    IReadOnlyList<RegistrationView> Registrations { get; }

    IReadOnlyList<LineStatus> Lines { get; }

    IReadOnlyList<LiveCall> LiveCalls { get; }

    int WebClients { get; }

    PresenceState PresenceOf(long userId);

    IReadOnlyDictionary<long, PresenceState> PresenceAll();
}

/// <summary>
/// The telephony engine: one SIP transport (UDP and TCP on the SIP port), the registrar of the phones, the registrations of the lines, the calls, the
/// subscriptions of the busy lamps and the web clients. It runs in the web process as a hosted service, reads the configuration from the database and reloads
/// it when the business layer says something changed; a change of the ports or the address restarts it.
/// </summary>
public sealed class PbxEngine : BackgroundService, ICallHost, IPbxStatus
{
    private static readonly string[] Scanners = ["friendly-scanner", "sipvicious", "sipcli", "sip-scan", "sundayddr", "iWar", "VaxSIPUserAgent", "pplsip"];

    private readonly ConfigStore _config;
    private readonly SecretProtector _secrets;
    private readonly WebClientHub _hub;
    private readonly ILogger<PbxEngine> _logger;
    private readonly SemaphoreSlim _reload = new(1, 1);
    private volatile PbxSnapshot _snapshot = PbxSnapshot.Empty;
    private volatile bool _reloadWanted = true;
    private SIPTransport? _transport;
    private SipContext? _context;
    private Registrar? _registrar;
    private LineManager? _lines;
    private Subscriptions? _subscriptions;
    private TelephonyConfig? _running;

    public PbxEngine(ConfigStore config, IServiceScopeFactory scopes, SecretProtector secrets, TelephonyLogWriter log, WebClientHub hub, TimeProvider clock, ILogger<PbxEngine> logger)
    {
        _config = config;
        Scopes = scopes;
        _secrets = secrets;
        Log = log;
        _hub = hub;
        Clock = clock;
        _logger = logger;
        Presence = new PresenceTracker();
        Calls = new CallManager(this);
        Mailboxes = new MailboxApp(() => _snapshot, scopes, log, clock, () => Calls);
        Presence.Attach(() => Calls.Calls, IsOnline, () => _snapshot);
        Presence.StateChanged += OnPresenceChanged;
        hub.OnMessage = OnWebMessageAsync;
        hub.ConnectionChanged += (connection, _) => Presence.Changed(connection.UserId);
        TelephonySignals.Changed += _ => _reloadWanted = true;
        TelephonySignals.UserStateChanged += OnUserStateChanged;
    }

    public PbxSnapshot Snapshot => _snapshot;

    public PresenceTracker Presence { get; }

    public MailboxApp Mailboxes { get; }

    public TelephonyLogWriter Log { get; }

    public IServiceScopeFactory Scopes { get; }

    public TimeProvider Clock { get; }

    public CallManager Calls { get; }

    // ---------------------------------------------------------------- status

    public bool IsRunning => _transport is not null;

    public string? Problem { get; private set; }

    public IReadOnlyList<RegistrationView> Registrations
        => _registrar is null ? [] : [.. _registrar.Bindings.OrderBy(b => b.DeviceName).Select(b => new RegistrationView(b.DeviceId, b.DeviceName, b.UserId, b.RemoteEndPoint.ToString(), b.UserAgent, b.Expires))];

    IReadOnlyList<LineStatus> IPbxStatus.Lines => _lines?.Status(_snapshot, id => Calls.Calls.Count(c => c.LineId == id && !c.IsOver)) ?? [];

    IReadOnlyList<LiveCall> IPbxStatus.LiveCalls => [.. Calls.Calls.Where(c => !c.IsOver).Select(c => new LiveCall(c.Id, c.Direction, c.Caller.Number, c.Caller.Name, c.CalledNumber,
        c.B is null ? "ringing" : "connected", c.StartDate, c.AnswerDate))];

    public int WebClients => _hub.Count;

    public PresenceState PresenceOf(long userId) => Presence.Of(userId);

    public IReadOnlyDictionary<long, PresenceState> PresenceAll() => Presence.All();

    private bool IsOnline(long userId) => _hub.IsOnline(userId) || (_registrar?.ForUser(userId).Any() ?? false);

    // ---------------------------------------------------------------- life

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _config.Current.Telephony;
                if (_running is null || NeedsRestart(_running, settings))
                {
                    await RestartAsync(settings).ConfigureAwait(false);
                }

                if (_reloadWanted || _snapshot == PbxSnapshot.Empty)
                {
                    _reloadWanted = false;
                    await ReloadAsync(stoppingToken).ConfigureAwait(false);
                }

                _registrar?.Prune(_snapshot);
                Presence.RefreshAll();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Problem = ex.Message;
                Log.Error(LogCategory.System, "The telephony engine had a problem", ex.ToString());
            }

            try
            {
                // A change the business layer signalled is picked up within a second; otherwise the housekeeping runs every 15 seconds.
                for (var i = 0; i < 15 && !_reloadWanted && !NeedsRestart(_running, _config.Current.Telephony); i++)
                {
                    await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Stop();
    }

    private static bool NeedsRestart(TelephonyConfig? running, TelephonyConfig now)
        => running is null || running.SipPort != now.SipPort || running.RtpPortFrom != now.RtpPortFrom || running.RtpPortTo != now.RtpPortTo
           || running.PublicAddress != now.PublicAddress || running.SipTrace != now.SipTrace;

    private async Task RestartAsync(TelephonyConfig settings)
    {
        Stop();
        _running = new TelephonyConfig
        {
            SipPort = settings.SipPort, RtpPortFrom = settings.RtpPortFrom, RtpPortTo = settings.RtpPortTo, PublicAddress = settings.PublicAddress, SipTrace = settings.SipTrace,
        };
        var publicAddress = await ResolvePublicAddressAsync(settings.PublicAddress).ConfigureAwait(false);
        Problem = string.IsNullOrWhiteSpace(settings.PublicAddress) ? "no-public-address" : null;
        try
        {
            var transport = new SIPTransport();
            transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, settings.SipPort)));
            transport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, settings.SipPort)));
            // TLS only out, to lines that want it (a phone of this installation speaks UDP or TCP).
            transport.AddSIPChannel(new SIPTLSChannel(new IPEndPoint(IPAddress.Any, 0)) { BypassCertificateValidation = false });
            if (publicAddress is not null)
            {
                transport.ContactHost = publicAddress.ToString();
            }

            transport.SIPTransportRequestReceived += OnRequestAsync;
            if (settings.SipTrace)
            {
                transport.SIPRequestInTraceEvent += (_, remote, request) => Log.Info(LogCategory.System, $"SIP in {request.Method} {request.URI}", request.ToString(), remote.ToString());
                transport.SIPRequestOutTraceEvent += (_, remote, request) => Log.Info(LogCategory.System, $"SIP out {request.Method} {request.URI}", request.ToString(), remote.ToString());
                transport.SIPResponseInTraceEvent += (_, remote, response) => Log.Info(LogCategory.System, $"SIP in {response.StatusCode} {response.ReasonPhrase}", response.ToString(), remote.ToString());
                transport.SIPResponseOutTraceEvent += (_, remote, response) => Log.Info(LogCategory.System, $"SIP out {response.StatusCode} {response.ReasonPhrase}", response.ToString(), remote.ToString());
            }

            _transport = transport;
            _context = new SipContext(transport, new MediaPorts(settings.RtpPortFrom, settings.RtpPortTo, publicAddress), Log);
            _registrar = new Registrar(transport, Log, Scopes, Clock) { MaxExpiry = settings.MaxRegisterSeconds, MaxFailures = _config.Current.Security.SipMaxFailures };
            _registrar.Changed += user =>
            {
                if (user is { } u)
                {
                    Presence.Changed(u);
                }
            };
            _lines = new LineManager(transport, Log, Scopes, Clock);
            _subscriptions = new Subscriptions(transport, Log, Scopes, Clock);
            _reloadWanted = true;
            Log.Info(LogCategory.System, $"The telephony engine listens on port {settings.SipPort} (UDP, TCP), audio on {settings.RtpPortFrom}–{settings.RtpPortTo}",
                publicAddress is null ? "No public address is set: phones and providers outside this machine may not reach the audio." : $"Public address {publicAddress}");
        }
        catch (SocketException ex)
        {
            Problem = ex.Message;
            Log.Error(LogCategory.System, $"The SIP port {settings.SipPort} could not be opened", ex.Message);
            Stop();
        }
    }

    private void Stop()
    {
        foreach (var call in Calls.Calls)
        {
            Calls.Hangup(call.A);
            if (call.B is { } b)
            {
                Calls.Hangup(b);
            }
        }

        _lines?.StopAll();
        _transport?.Shutdown();
        _transport = null;
        _context = null;
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        await _reload.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = Scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
            _snapshot = await PbxSnapshot.LoadAsync(db, _secrets, _config.Current.Telephony, ct).ConfigureAwait(false);
            if (_lines is not null && _context is not null)
            {
                await _lines.ApplyAsync(_snapshot, _context.PublicAddress, _config.Current.Telephony.SipPort).ConfigureAwait(false);
            }

            Presence.RefreshAll();
        }
        finally
        {
            _reload.Release();
        }
    }

    private static async Task<IPAddress?> ResolvePublicAddressAsync(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (IPAddress.TryParse(configured.Trim(), out var ip))
            {
                return ip;
            }

            try
            {
                return (await Dns.GetHostAddressesAsync(configured.Trim()).ConfigureAwait(false)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (SocketException)
            {
                return null;
            }
        }

        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
    }

    private void OnUserStateChanged(long userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ReloadAsync(CancellationToken.None).ConfigureAwait(false);
                Presence.Changed(userId);
                await SendMeAsync(userId).ConfigureAwait(false);
                if (_subscriptions is { } subscriptions)
                {
                    await subscriptions.MessagesChangedAsync(userId, _snapshot, Presence).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The state of user {UserId} could not be refreshed.", userId);
            }
        });
    }

    private void OnPresenceChanged(long userId, PresenceState state)
    {
        _hub.Broadcast(new JsonObject { ["t"] = "presence", ["id"] = userId, ["state"] = Name(state) });
        if (_subscriptions is { } subscriptions)
        {
            _ = subscriptions.PresenceChangedAsync(userId, _snapshot, Presence);
        }
    }

    private static string Name(PresenceState state) => state switch
    {
        PresenceState.Idle => "idle",
        PresenceState.Ringing => "ringing",
        PresenceState.Busy => "busy",
        PresenceState.DoNotDisturb => "dnd",
        _ => "offline",
    };

    // ---------------------------------------------------------------- SIP requests

    private async Task OnRequestAsync(SIPEndPoint local, SIPEndPoint remote, SIPRequest request)
    {
        if (_transport is not { } transport || _registrar is not { } registrar)
        {
            return;
        }

        try
        {
            if (registrar.IsBlocked(remote))
            {
                return;
            }

            var agent = request.Header.UserAgent ?? string.Empty;
            if (Scanners.Any(s => agent.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            switch (request.Method)
            {
                case SIPMethodsEnum.REGISTER:
                    if (await registrar.AuthenticateAsync(_snapshot, local, remote, request).ConfigureAwait(false) is { } device)
                    {
                        await registrar.RegisterAsync(device, request, remote).ConfigureAwait(false);
                    }

                    break;
                case SIPMethodsEnum.INVITE when request.Header.To?.ToTag is { Length: > 0 }:
                    await ReinviteAsync(request).ConfigureAwait(false);
                    break;
                case SIPMethodsEnum.INVITE:
                    await NewCallAsync(local, remote, request).ConfigureAwait(false);
                    break;
                case SIPMethodsEnum.BYE:
                    await ByeAsync(request).ConfigureAwait(false);
                    break;
                case SIPMethodsEnum.ACK:
                    break;
                case SIPMethodsEnum.OPTIONS or SIPMethodsEnum.NOTIFY:
                    await transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null)).ConfigureAwait(false);
                    break;
                case SIPMethodsEnum.SUBSCRIBE:
                    if (await registrar.AuthenticateAsync(_snapshot, local, remote, request).ConfigureAwait(false) is { } subscriber && _subscriptions is { } subscriptions)
                    {
                        await subscriptions.SubscribeAsync(_snapshot, subscriber, request, remote, Presence).ConfigureAwait(false);
                    }

                    break;
                case SIPMethodsEnum.REFER:
                    await ReferAsync(request).ConfigureAwait(false);
                    break;
                case SIPMethodsEnum.INFO:
                    await InfoAsync(request).ConfigureAwait(false);
                    break;
                default:
                    await transport.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.MethodNotAllowed, null)).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogCategory.System, $"The request {request.Method} could not be handled", ex.ToString(), remote.ToString());
        }
    }

    private async Task NewCallAsync(SIPEndPoint local, SIPEndPoint remote, SIPRequest request)
    {
        var s = _snapshot;
        var context = _context!;
        var localTime = Fmt.Local(Clock.GetUtcNow().UtcDateTime);
        var lineId = _lines!.LineOfContactUser(request.URI.User) ?? _lines.LineOfAddress(remote.Address);
        if (lineId is { } id && s.Line(id) is { } line)
        {
            var callerRaw = request.Header.PassertedIdentity?.FirstOrDefault()?.URI?.User ?? request.Header.From?.FromURI?.User;
            var callerNumber = NumberRules.ToE164(callerRaw, s.Plan) ?? NumberRules.Clean(callerRaw);
            var calledRaw = request.Header.To?.ToURI?.User is { Length: > 0 } to && !to.StartsWith("mp", StringComparison.Ordinal) ? to : request.URI.User;
            var called = NumberRules.ToE164(calledRaw, s.Plan) ?? NumberRules.Clean(calledRaw);
            var uas = new SIPServerUserAgent(_transport!, null, new UASInviteTransaction(_transport!, request, null), null);
            var leg = new SipInboundLeg(context, uas) { Kind = LegKind.Line, LineId = line.Id, Label = line.Name };
            if (!leg.AcceptOffer())
            {
                leg.Reject(488, "Not Acceptable Here");
                return;
            }

            var callerName = request.Header.From?.FromName;
            var callerId = new CallerId(string.IsNullOrEmpty(callerNumber) ? null : callerNumber, string.IsNullOrWhiteSpace(callerName) ? null : callerName, called);
            var route = Router.FromLine(s, line, called, callerNumber, localTime);
            Calls.Start(leg, callerId, route, CallDirection.Inbound, called, line.Id);
            return;
        }

        if (await _registrar!.AuthenticateAsync(s, local, remote, request).ConfigureAwait(false) is not { } device)
        {
            return;
        }

        var server = new SIPServerUserAgent(_transport!, null, new UASInviteTransaction(_transport!, request, null), null);
        var caller = new SipInboundLeg(context, server) { Kind = LegKind.Device, DeviceId = device.Id, UserId = device.UserId, Label = device.Name };
        if (device.UserId is not { } userId || !s.Users.TryGetValue(userId, out var user))
        {
            caller.Reject(403, "Forbidden");
            return;
        }

        if (!caller.AcceptOffer())
        {
            caller.Reject(488, "Not Acceptable Here");
            return;
        }

        var dialled = request.URI.User ?? string.Empty;
        StartInternalCall(caller, user, dialled, localTime);
    }

    private void StartInternalCall(Leg caller, UserInfo user, string dialled, DateTime localTime)
    {
        var route = Router.FromInternal(_snapshot, user.Id, dialled, localTime);
        var callerId = new CallerId(user.Extension, user.Name, NumberRules.Clean(dialled), user.Id);
        var direction = route is RouteExternal ? CallDirection.Outbound : CallDirection.Internal;
        Calls.Start(caller, callerId, route, direction, NumberRules.Clean(dialled), (route as RouteExternal)?.Line.Id);
    }

    private Leg? FindDialogLeg(SIPRequest request)
    {
        foreach (var call in Calls.Calls)
        {
            foreach (var leg in new[] { call.A, call.B }.Concat(call.Ringing.Values))
            {
                if (leg is not null && leg.Matches(request))
                {
                    return leg;
                }
            }
        }

        return null;
    }

    private async Task ByeAsync(SIPRequest request)
    {
        var leg = FindDialogLeg(request);
        await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, leg is null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist : SIPResponseStatusCodesEnum.Ok, null))
            .ConfigureAwait(false);
        switch (leg)
        {
            case SipInboundLeg inbound:
                inbound.RemoteHungUp();
                break;
            case SipOutboundLeg outbound:
                outbound.RemoteHungUp();
                break;
        }
    }

    /// <summary>A re-INVITE (hold, a new address): the leg answers with its own SDP for the new offer; the audio keeps flowing through the bridge.</summary>
    private async Task ReinviteAsync(SIPRequest request)
    {
        var leg = FindDialogLeg(request);
        var tx = new UASInviteTransaction(_transport!, request, null);
        if (leg?.Media is not { } media || string.IsNullOrWhiteSpace(request.Body))
        {
            tx.SendFinalResponse(SIPResponse.GetResponse(request, leg is null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist : SIPResponseStatusCodesEnum.NotAcceptableHere, null));
            return;
        }

        var offer = SIPSorcery.Net.SDP.ParseSDPDescription(request.Body);
        media.Session.SetRemoteDescription(SdpType.offer, offer);
        var answer = media.Session.CreateAnswer(_context?.PublicAddress);
        var ok = tx.GetOkResponse(SIPSorcery.Net.SDP.SDP_MIME_CONTENTTYPE, answer.ToString());
        tx.SendFinalResponse(ok);
        var hold = offer.Media.FirstOrDefault()?.MediaStreamStatus is SIPSorcery.Net.MediaStreamStatusEnum.SendOnly or SIPSorcery.Net.MediaStreamStatusEnum.Inactive;
        if (Calls.FindByLeg(leg)?.Other(leg) is WebLeg web)
        {
            web.NotifyRemoteHold(hold);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>A transfer of a phone (REFER): accepted, the other party goes to the Refer-To, the phone that transferred is hung up.</summary>
    private async Task ReferAsync(SIPRequest request)
    {
        var leg = FindDialogLeg(request);
        var target = SIPUserField.ParseSIPUserField(request.Header.ReferTo ?? string.Empty)?.URI?.User;
        if (leg is null || string.IsNullOrWhiteSpace(target))
        {
            await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, leg is null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist : SIPResponseStatusCodesEnum.BadRequest, null))
                .ConfigureAwait(false);
            return;
        }

        await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Accepted, null)).ConfigureAwait(false);
        var dialogue = (leg as SipInboundLeg)?.Dialogue ?? (leg as SipOutboundLeg)?.Dialogue;
        if (dialogue is not null)
        {
            // The phone waits for the outcome of its REFER: it is told at once that the transfer went through (it is not part of the call any more).
            var notify = dialogue.GetInDialogRequest(SIPMethodsEnum.NOTIFY);
            notify.Header.Event = "refer";
            notify.Header.SubscriptionState = "terminated;reason=noresource";
            notify.Header.ContentType = "message/sipfrag;version=2.0";
            notify.Body = "SIP/2.0 200 OK\r\n";
            new SIPNonInviteTransaction(_transport!, notify, null).SendRequest();
        }

        if (!Calls.Transfer(leg, target))
        {
            Log.Info(LogCategory.Call, $"A transfer to {target} was refused", userId: leg.UserId, deviceId: leg.DeviceId);
        }
    }

    /// <summary>DTMF as SIP INFO (some phones send keys so): for the mailbox.</summary>
    private async Task InfoAsync(SIPRequest request)
    {
        var leg = FindDialogLeg(request);
        await _transport!.SendResponseAsync(SIPResponse.GetResponse(request, leg is null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist : SIPResponseStatusCodesEnum.Ok, null))
            .ConfigureAwait(false);
        var body = request.Body ?? string.Empty;
        var signal = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Signal=", StringComparison.OrdinalIgnoreCase))?[7..].Trim();
        if (leg?.Media is { } media && signal is { Length: 1 })
        {
            media.InjectDigit(signal[0]);
        }
    }

    // ---------------------------------------------------------------- legs for the call manager

    public IReadOnlyList<Leg> LegsForUser(long userId, CallerId caller)
    {
        var legs = new List<Leg>();
        if (_context is not { } context || !_snapshot.Users.TryGetValue(userId, out var user))
        {
            return legs;
        }

        var host = context.PublicAddress?.ToString() ?? "matphone";
        var shownNumber = caller.Number ?? "anonymous";
        var shownName = (caller.Name ?? string.Empty).Replace("\"", string.Empty, StringComparison.Ordinal);
        foreach (var binding in _registrar!.ForUser(userId))
        {
            var device = _snapshot.DevicesBySipUser.Values.FirstOrDefault(d => d.Id == binding.DeviceId);
            var from = shownName.Length > 0 ? $"\"{shownName}\" <sip:{shownNumber}@{host}>" : $"<sip:{shownNumber}@{host}>";
            var target = new SipTarget(binding.Contact.ToString(), binding.RemoteEndPoint, from, $"<sip:{device?.SipUser ?? "phone"}@{host}>", null, null, null, []);
            legs.Add(new SipOutboundLeg(context, target) { Kind = LegKind.Device, UserId = userId, DeviceId = binding.DeviceId, Label = binding.DeviceName });
        }

        if (user.WebClientEnabled)
        {
            foreach (var connection in _hub.Of(userId).Where(c => c.CanRing))
            {
                legs.Add(new WebLeg(connection, context.Ports, Log) { Kind = LegKind.Web, UserId = userId, Label = "Browser" });
            }
        }

        return legs;
    }

    public Leg LegForLine(RouteExternal route, CallerId caller)
        => new SipOutboundLeg(_context!, _lines!.TargetOf(route, caller, _snapshot.Plan)) { Kind = LegKind.Line, LineId = route.Line.Id, Label = route.Line.Name };

    // ---------------------------------------------------------------- the web client

    private async Task OnWebMessageAsync(WebClientConnection connection, JsonObject message)
    {
        var type = message["t"]?.GetValue<string>();
        var legId = message["leg"]?.GetValue<string>();
        var leg = legId is null ? null : FindWebLeg(connection, legId);
        switch (type)
        {
            case "hello":
                connection.CanRing = message["ring"]?.GetValue<bool>() ?? true;
                await WelcomeAsync(connection).ConfigureAwait(false);
                break;
            case "dial":
                _ = DialInBackgroundAsync(connection, message["number"]?.GetValue<string>() ?? string.Empty);
                break;
            case "answer":
                leg?.OnAnswer(message["sdp"]?.GetValue<string>());
                break;
            case "reject":
                leg?.OnAnswer(null);
                break;
            case "hangup":
                if (leg is { State: LegState.Answered } || leg?.IsOutgoing == true)
                {
                    Calls.Hangup(leg);
                }
                else
                {
                    leg?.OnAnswer(null);
                }

                break;
            case "candidate":
                if (message["candidate"] is JsonNode candidate)
                {
                    leg?.OnCandidate(candidate.ToJsonString());
                }

                break;
            case "dtmf":
                if (leg is not null && message["digit"]?.GetValue<string>() is { Length: 1 } digit)
                {
                    var code = digit[0] switch { '*' => (byte)10, '#' => (byte)11, var c and >= '0' and <= '9' => (byte)(c - '0'), _ => (byte)255 };
                    if (code != 255)
                    {
                        // The key goes to what answers the browser itself (the mailbox) and to the other party of a call.
                        leg.Media?.InjectDigit(digit[0]);
                        if (Calls.FindByLeg(leg)?.Other(leg)?.Media is { } other)
                        {
                            await other.Session.SendDtmf(code, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                }

                break;
            case "hold":
                if (leg is not null && Calls.FindByLeg(leg)?.Other(leg) is { } holdOther)
                {
                    var on = message["on"]?.GetValue<bool>() ?? true;
                    leg.SetHold(on);
                    if (holdOther.Media is { } media)
                    {
                        media.Muted = on;
                    }
                }

                break;
            case "transfer":
                if (leg is null || !Calls.Transfer(leg, message["number"]?.GetValue<string>() ?? string.Empty))
                {
                    connection.Send(new JsonObject { ["t"] = "error", ["message"] = "transfer" });
                }

                break;
            case "pickup":
                var colleague = message["user"]?.GetValue<long>() ?? 0;
                if (_context is not { } context || !Calls.Pickup(colleague, _ => new WebLeg(connection, context.Ports, Log) { Kind = LegKind.Web, UserId = connection.UserId, Label = "Browser" }))
                {
                    connection.Send(new JsonObject { ["t"] = "error", ["message"] = "pickup" });
                }

                break;
            case "dnd":
                await ActAsync(connection, (db, actor) => Business.Users.ToggleDoNotDisturbAsync(db, actor, connection.UserId, message["on"]?.GetValue<bool>())).ConfigureAwait(false);
                break;
            case "forward":
                var forwardTo = message["number"]?.GetValue<string>() ?? string.Empty;
                if (NumberRules.Clean(forwardTo).Length > 0)
                {
                    await ActAsync(connection, (db, actor) => Business.Users.ToggleForwardAsync(db, actor, connection.UserId, forwardTo)).ConfigureAwait(false);
                }

                break;
            case "group":
                var groupId = message["group"]?.GetValue<long>() ?? 0;
                await ActAsync(connection, (db, actor) => CallGroups.SetLoggedInAsync(db, actor, connection.UserId, groupId, message["on"]?.GetValue<bool>())).ConfigureAwait(false);
                break;
            case "ring":
                connection.CanRing = message["on"]?.GetValue<bool>() ?? true;
                break;
            case "ping":
                connection.Send(new JsonObject { ["t"] = "pong" });
                break;
        }
    }

    private WebLeg? FindWebLeg(WebClientConnection connection, string legId)
    {
        if (_settingUp.TryGetValue(legId, out var pending))
        {
            return pending.Connection == connection ? pending : null;
        }

        return Calls.Calls.SelectMany(c => new[] { c.A, c.B }.Concat(c.Ringing.Values)).OfType<WebLeg>()
            .FirstOrDefault(l => l.Id == legId && l.Connection.UserId == connection.UserId);
    }

    // The calls a browser dialled whose audio is still being set up (they belong to no call yet): its answer and candidates find them here.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WebLeg> _settingUp = new();

    // Runs beside the messages of the socket: the answer of the browser comes in over the same socket while the leg waits for it.
    private async Task DialInBackgroundAsync(WebClientConnection connection, string number)
    {
        try
        {
            await DialAsync(connection, number).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(LogCategory.Call, "A call from the browser could not be started", ex.Message, userId: connection.UserId);
            connection.Send(new JsonObject { ["t"] = "error", ["message"] = "failed" });
        }
    }

    private async Task DialAsync(WebClientConnection connection, string number)
    {
        if (_context is not { } context || !_snapshot.Users.TryGetValue(connection.UserId, out var user) || !user.WebClientEnabled)
        {
            connection.Send(new JsonObject { ["t"] = "error", ["message"] = "not-allowed" });
            return;
        }

        if (NumberRules.Clean(number).Length == 0)
        {
            return;
        }

        var leg = new WebLeg(connection, context.Ports, Log) { Kind = LegKind.Web, UserId = user.Id, Label = "Browser" };
        _settingUp[leg.Id] = leg;
        bool ready;
        try
        {
            ready = await leg.StartOutgoingAsync(number).ConfigureAwait(false);
        }
        finally
        {
            _settingUp.TryRemove(leg.Id, out _);
        }

        if (!ready || leg.IsEnded)
        {
            leg.Reject(408, "Request Timeout");
            return;
        }

        StartInternalCall(leg, user, number, Fmt.Local(Clock.GetUtcNow().UtcDateTime));
    }

    private async Task ActAsync(WebClientConnection connection, Func<MpDbContext, Actor, Task> action)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
        await action(db, new Actor(connection.UserId, connection.Name, false)).ConfigureAwait(false);
    }

    private async Task WelcomeAsync(WebClientConnection connection)
    {
        var presence = new JsonObject();
        foreach (var (id, state) in Presence.All())
        {
            presence[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Name(state);
        }

        connection.Send(new JsonObject { ["t"] = "welcome", ["presence"] = presence, ["engine"] = IsRunning });
        await SendMeAsync(connection.UserId, connection).ConfigureAwait(false);
    }

    /// <summary>The own state of a person (DND, forwarding, groups, new messages), to all of the person's web clients.</summary>
    private async Task SendMeAsync(long userId, WebClientConnection? only = null)
    {
        if (!_hub.Of(userId).Any())
        {
            return;
        }

        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
        var actor = new Actor(userId, null, false);
        var groups = await CallGroups.MembershipsAsync(db, actor, userId).ConfigureAwait(false);
        var unheard = await Business.Mailboxes.NewCountAsync(db, actor).ConfigureAwait(false);
        var user = _snapshot.Users.GetValueOrDefault(userId);
        var message = new JsonObject
        {
            ["t"] = "me",
            ["dnd"] = user?.DoNotDisturb ?? false,
            ["forward"] = user?.ForwardAlways,
            ["voicemail"] = unheard,
            ["groups"] = new JsonArray([.. groups.Select(g => (JsonNode)new JsonObject { ["id"] = g.GroupId, ["name"] = g.GroupName, ["number"] = g.Number, ["on"] = g.IsLoggedIn })]),
        };
        if (only is not null)
        {
            only.Send(message);
        }
        else
        {
            _hub.SendTo(userId, message);
        }
    }
}
