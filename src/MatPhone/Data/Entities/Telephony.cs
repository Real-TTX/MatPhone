namespace MatPhone.Data.Entities;

/// <summary>A team: people who work together and see each other in the panel of the web client. A team routes no calls (that is a <see cref="CallGroup"/>).</summary>
public class Team : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>The mark of the team in lists ("#d6336c"), chosen by the administrator; empty draws none.</summary>
    public string? Color { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<TeamMember> Members { get; set; } = [];
}

public class TeamMember : AuditedEntity
{
    public long TeamId { get; set; }

    public Team? Team { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }
}

/// <summary>How a group rings its members.</summary>
public enum RingStrategy
{
    /// <summary>All logged-in members at once; the first who answers takes the call.</summary>
    Parallel,

    /// <summary>One after the other in the order of the list, each for <see cref="CallGroup.RingSeconds"/>.</summary>
    Linear,

    /// <summary>One after the other, starting after the member who took the last call.</summary>
    RoundRobin,
}

/// <summary>Where a call goes that nobody took (a group that rang out, a user who did not answer).</summary>
public enum FallbackKind
{
    Hangup,
    Mailbox,
    User,
    Group,
    External,
}

/// <summary>A call group ("Gruppe"): a number rings its members by a strategy; members log in and out.</summary>
public class CallGroup : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public RingStrategy Strategy { get; set; } = RingStrategy.Parallel;

    /// <summary>Linear and round robin: how long one member rings before the next one.</summary>
    public int RingSeconds { get; set; } = 15;

    /// <summary>How long a caller waits in all before the fallback.</summary>
    public int MaxWaitSeconds { get; set; } = 60;

    public FallbackKind Fallback { get; set; } = FallbackKind.Hangup;

    public long? FallbackUserId { get; set; }

    public long? FallbackGroupId { get; set; }

    public long? FallbackMailboxId { get; set; }

    public string? FallbackNumber { get; set; }

    /// <summary>Put in front of the caller's name on the phones of the members ("Support: "), so they know which line rings.</summary>
    public string? CallerNamePrefix { get; set; }

    public bool IsActive { get; set; } = true;

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<CallGroupMember> Members { get; set; } = [];
}

public class CallGroupMember : AuditedEntity
{
    public long CallGroupId { get; set; }

    public CallGroup? CallGroup { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The order for linear and round robin (0 first).</summary>
    public int Position { get; set; }

    /// <summary>"Eingebucht": only logged-in members ring. A member logs in and out with a function key, the web client or the feature code.</summary>
    public bool IsLoggedIn { get; set; } = true;

    /// <summary>When this member took a call of the group last: the start of the next round robin.</summary>
    public DateTime? LastCallDate { get; set; }
}

public enum NumberKind
{
    /// <summary>An extension ("201"): dialled inside the installation.</summary>
    Internal,

    /// <summary>A number of a line ("+4930123456789"): reached from outside.</summary>
    External,
}

/// <summary>What a number or a rule sends a call to.</summary>
public enum TargetKind
{
    None,
    User,
    Group,
    Mailbox,
    External,
}

/// <summary>A number and what it rings. Internal numbers are extensions; external numbers arrive on a line.</summary>
public class PhoneNumber : AuditedEntity, ISoftDeletable
{
    /// <summary>Normalised: digits for an extension, E.164 with a plus for an external number.</summary>
    public string Number { get; set; } = "";

    public NumberKind Kind { get; set; } = NumberKind.Internal;

    public string? Description { get; set; }

    /// <summary>External: the line it arrives on (null: any line).</summary>
    public long? LineId { get; set; }

    public Line? Line { get; set; }

    public TargetKind Target { get; set; } = TargetKind.None;

    public long? UserId { get; set; }

    public User? User { get; set; }

    public long? CallGroupId { get; set; }

    public CallGroup? CallGroup { get; set; }

    public long? MailboxId { get; set; }

    public Mailbox? Mailbox { get; set; }

    /// <summary>Target external: the number a call is passed on to.</summary>
    public string? TargetNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

public enum DeviceKind
{
    DeskPhone,
    Softphone,
    Dect,
    Other,
}

/// <summary>A SIP phone that registers here with its own name and password and rings for its user.</summary>
public class Device : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public DeviceKind Kind { get; set; } = DeviceKind.DeskPhone;

    public string? Vendor { get; set; }

    public string? Model { get; set; }

    /// <summary>Lower case without separators ("805ec0a1b2c3"), for provisioning; optional.</summary>
    public string? MacAddress { get; set; }

    /// <summary>The SIP user name the phone registers with (unique).</summary>
    public string SipUser { get; set; } = "";

    /// <summary>The SIP password, protected with the key ring of the application (the digest needs the plain text).</summary>
    public string SipPasswordProtected { get; set; } = "";

    public long? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The function keys of this device; null: those of its user.</summary>
    public long? KeyTemplateId { get; set; }

    public KeyTemplate? KeyTemplate { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime? LastRegisterDate { get; set; }

    public string? LastUserAgent { get; set; }

    public string? LastAddress { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>A voice mailbox of a user or of a group: greeting, messages, the PIN for the phone and the mail of a new message.</summary>
public class Mailbox : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public long? UserId { get; set; }

    public User? User { get; set; }

    public long? CallGroupId { get; set; }

    public CallGroup? CallGroup { get; set; }

    /// <summary>The PIN for listening from a phone (digits), hashed.</summary>
    public string? PinHash { get; set; }

    /// <summary>The recorded greeting (8 kHz WAV); null: a short tone only.</summary>
    public long? GreetingFileId { get; set; }

    public StoredFile? GreetingFile { get; set; }

    public int MaxMessageSeconds { get; set; } = 120;

    /// <summary>A new message is sent by mail to <see cref="MailAddress"/>.</summary>
    public bool MailNotify { get; set; }

    public string? MailAddress { get; set; }

    /// <summary>The recording travels with the mail (WAV).</summary>
    public bool MailAttachAudio { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

public class MailboxMessage : AuditedEntity, ISoftDeletable
{
    public long MailboxId { get; set; }

    public Mailbox? Mailbox { get; set; }

    public string? CallerNumber { get; set; }

    public string? CallerName { get; set; }

    public DateTime ReceivedDate { get; set; }

    public int DurationSeconds { get; set; }

    public long? AudioFileId { get; set; }

    public StoredFile? AudioFile { get; set; }

    /// <summary>Not listened to yet.</summary>
    public bool IsNew { get; set; } = true;

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

public enum SipTransport
{
    Udp,
    Tcp,
    Tls,
}

/// <summary>Which header carries the own number on a call that goes out over a line.</summary>
public enum CallerIdMode
{
    FromHeader,
    PAssertedIdentity,
    PPreferredIdentity,
}

/// <summary>How a provider wants numbers written in the request line.</summary>
public enum NumberFormat
{
    /// <summary>"+4930123456"</summary>
    E164Plus,

    /// <summary>"4930123456"</summary>
    E164,

    /// <summary>"030123456"</summary>
    National,
}

/// <summary>
/// A provider profile ("Telekom", "peoplefone", "Vodafone" ...): the settings a line of that provider starts from. The built-in ones are written on the first start
/// and can be changed; a line copies them when it is created and keeps its own values.
/// </summary>
public class LineProfile : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    /// <summary>Written by the application (the provider table); can be changed but not deleted.</summary>
    public bool IsBuiltIn { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 5060;

    public SipTransport Transport { get; set; } = SipTransport.Udp;

    public bool Register { get; set; } = true;

    public int RegisterExpirySeconds { get; set; } = 600;

    public string? OutboundProxy { get; set; }

    public CallerIdMode CallerIdMode { get; set; } = CallerIdMode.PAssertedIdentity;

    public NumberFormat NumberFormat { get; set; } = NumberFormat.E164Plus;

    /// <summary>What the provider calls the user name (shown as the hint of the field: "Ihre Rufnummer im Format +49...").</summary>
    public string? UserNameHint { get; set; }

    /// <summary>The SIP domain when it is not the host ("tel.t-online.de").</summary>
    public string? Domain { get; set; }

    public string? Notes { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>A line ("Leitung"): a SIP account or trunk at a provider. It registers there and carries the calls to and from the outside.</summary>
public class Line : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public long? ProfileId { get; set; }

    public LineProfile? Profile { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 5060;

    public SipTransport Transport { get; set; } = SipTransport.Udp;

    public string? Domain { get; set; }

    public string? OutboundProxy { get; set; }

    public string UserName { get; set; } = "";

    /// <summary>The name for the authentication when it differs from <see cref="UserName"/>.</summary>
    public string? AuthUserName { get; set; }

    public string PasswordProtected { get; set; } = "";

    public bool Register { get; set; } = true;

    public int RegisterExpirySeconds { get; set; } = 600;

    public CallerIdMode CallerIdMode { get; set; } = CallerIdMode.PAssertedIdentity;

    public NumberFormat NumberFormat { get; set; } = NumberFormat.E164Plus;

    /// <summary>The number shown on a call out when the user has none of this line (E.164).</summary>
    public string? DefaultCallerNumber { get; set; }

    /// <summary>How many calls the line carries at once (0: no limit).</summary>
    public int MaxChannels { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>The last registration result as the engine saw it ("200 OK", "403 Forbidden").</summary>
    public string? LastStatus { get; set; }

    public DateTime? LastStatusDate { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

public enum RuleDirection
{
    Inbound,
    Outbound,
}

public enum RuleAction
{
    /// <summary>Inbound: send the call to <see cref="Rule.Target"/>.</summary>
    Route,

    /// <summary>Inbound: the caller hears busy.</summary>
    Reject,

    /// <summary>Outbound: dial over <see cref="Rule.LineId"/>, with the rewrite of the number.</summary>
    UseLine,

    /// <summary>Outbound: the number may not be dialled.</summary>
    Block,
}

/// <summary>
/// A rule for calls in or out. The first enabled rule (by <see cref="Position"/>) whose conditions match decides; without one an inbound call goes to its number's
/// target and an outbound call over the first line. Conditions: the days and the time, a pattern of the number (the caller's inbound, the dialled outbound), the
/// number called (inbound) and who dials (outbound).
/// </summary>
public class Rule : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public RuleDirection Direction { get; set; } = RuleDirection.Inbound;

    public int Position { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>The days the rule applies (bit 0 = Sunday ... bit 6 = Saturday); 0 = every day.</summary>
    public int Days { get; set; }

    public TimeOnly? TimeFrom { get; set; }

    public TimeOnly? TimeTo { get; set; }

    /// <summary>A dial pattern ("0900*", "+49151*", "11X"): the caller's number inbound, the dialled number outbound; empty: any.</summary>
    public string? Pattern { get; set; }

    /// <summary>Inbound: the number called (null: every external number).</summary>
    public long? PhoneNumberId { get; set; }

    public PhoneNumber? PhoneNumber { get; set; }

    /// <summary>Outbound: who dials (null: everybody).</summary>
    public long? CallerUserId { get; set; }

    public RuleAction Action { get; set; } = RuleAction.Route;

    public TargetKind Target { get; set; } = TargetKind.None;

    public long? TargetUserId { get; set; }

    public long? TargetGroupId { get; set; }

    public long? TargetMailboxId { get; set; }

    public string? TargetNumber { get; set; }

    /// <summary>Outbound: the line of the call.</summary>
    public long? LineId { get; set; }

    public Line? Line { get; set; }

    /// <summary>Outbound: digits taken from the front of the dialled number.</summary>
    public int StripDigits { get; set; }

    /// <summary>Outbound: put in front of the number after stripping.</summary>
    public string? Prefix { get; set; }

    /// <summary>Outbound: the number shown to the called party instead of the user's (E.164).</summary>
    public string? CallerNumber { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>A set of function keys ("Funktionstasten-Vorlage"), given to users or devices.</summary>
public class KeyTemplate : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<FunctionKey> Keys { get; set; } = [];
}

public enum KeyKind
{
    /// <summary>Busy lamp field: shows a colleague (free, ringing, busy) and calls or picks up.</summary>
    BusyLamp,

    /// <summary>Calls a number.</summary>
    SpeedDial,

    /// <summary>Logs the user in or out of a group ("Gruppe ein/aus").</summary>
    GroupToggle,

    /// <summary>Turns "Nicht stören" on and off.</summary>
    DoNotDisturb,

    /// <summary>Turns the forwarding of all calls to <see cref="FunctionKey.Number"/> on and off.</summary>
    Forward,

    /// <summary>Opens the mailbox (the new messages light the key).</summary>
    Mailbox,
}

/// <summary>A key of a template, or a personal key of a user (after the keys of the template).</summary>
public class FunctionKey : AuditedEntity
{
    public long? KeyTemplateId { get; set; }

    public KeyTemplate? KeyTemplate { get; set; }

    public long? UserId { get; set; }

    public User? User { get; set; }

    public int Position { get; set; }

    public KeyKind Kind { get; set; } = KeyKind.BusyLamp;

    public string Label { get; set; } = "";

    public long? TargetUserId { get; set; }

    public User? TargetUser { get; set; }

    public long? TargetGroupId { get; set; }

    public CallGroup? TargetGroup { get; set; }

    public string? Number { get; set; }
}

public enum CallDirection
{
    Inbound,
    Outbound,
    Internal,
}

public enum CallResult
{
    Answered,
    Missed,
    Busy,
    Rejected,
    Failed,
    Voicemail,
    Cancelled,
}

/// <summary>One call as the log keeps it ("Anrufprotokoll"): who called whom, when, how long, how it ended.</summary>
public class CallRecord : AuditedEntity
{
    /// <summary>The id of the call in the engine (also the SIP Call-ID of the first leg).</summary>
    public string CallId { get; set; } = "";

    public CallDirection Direction { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? AnswerDate { get; set; }

    public DateTime? EndDate { get; set; }

    public int DurationSeconds { get; set; }

    public string? CallerNumber { get; set; }

    public string? CallerName { get; set; }

    public string? CalledNumber { get; set; }

    public CallResult Result { get; set; }

    public long? LineId { get; set; }

    public long? CallGroupId { get; set; }

    public long? AnsweredByUserId { get; set; }

    public List<CallParticipant> Participants { get; set; } = [];
}

/// <summary>A user a call concerns: the caller, or whom it rang. The call list of a person is made of these.</summary>
public class CallParticipant : AuditedEntity
{
    public long CallRecordId { get; set; }

    public CallRecord? CallRecord { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public bool IsCaller { get; set; }

    public bool Answered { get; set; }
}

public enum LogSeverity
{
    Info,
    Warning,
    Error,
}

public enum LogCategory
{
    System,
    Registration,
    Line,
    Call,
    Mailbox,
    Security,
}

/// <summary>What the telephony engine reports: registrations, lines, calls that failed, refused requests. Pruned after the days of the log settings.</summary>
public class TelephonyLog : AuditedEntity
{
    public DateTime OccurredDate { get; set; }

    public LogSeverity Level { get; set; } = LogSeverity.Info;

    public LogCategory Category { get; set; } = LogCategory.System;

    public string Message { get; set; } = "";

    public string? Details { get; set; }

    public string? RemoteAddress { get; set; }

    public long? LineId { get; set; }

    public long? DeviceId { get; set; }

    public long? UserId { get; set; }
}
