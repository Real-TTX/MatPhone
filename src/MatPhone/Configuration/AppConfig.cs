using System.Text.Json.Serialization;

namespace MatPhone.Configuration;

/// <summary>
/// The configuration (JSON, <c>{data}/config/app.json</c>): how this installation is deployed and what the administrator set up.
/// The sections <see cref="Database"/> and <see cref="Server"/> can be overridden by environment variables
/// (<c>MATPHONE__Database__Host</c>); the rest is edited on the administration pages. What the telephony does with calls (numbers, groups, rules) is
/// logic and lives in the database.
/// </summary>
public sealed class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();

    public ServerConfig Server { get; set; } = new();

    public CompanyConfig Company { get; set; } = new();

    public TelephonyConfig Telephony { get; set; } = new();

    public MailConfig Mail { get; set; } = new();

    public StorageConfig Storage { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public BackupConfig Backup { get; set; } = new();

    public LogConfig Logs { get; set; } = new();
}

/// <summary>The company the installation belongs to: its name is shown in the shell and signs the mails of the mailboxes.</summary>
public sealed class CompanyConfig
{
    public string Name { get; set; } = "";
}

/// <summary>
/// How the telephony engine is reached and how numbers are read. The engine listens for SIP on <see cref="SipPort"/> (UDP and TCP) and sends audio from the ports
/// <see cref="RtpPortFrom"/> to <see cref="RtpPortTo"/>; all of them must be open (and published by Docker). A change restarts the engine.
/// </summary>
public sealed class TelephonyConfig
{
    public int SipPort { get; set; } = 5060;

    public int RtpPortFrom { get; set; } = 6100;

    public int RtpPortTo { get; set; } = 6199;

    /// <summary>
    /// The address phones, browsers and providers send to (an IP address or a host name of this server, as seen from them). Empty: the first address of the
    /// machine; behind NAT or in a container it has to be set.
    /// </summary>
    public string PublicAddress { get; set; } = "";

    /// <summary>The country code without plus ("49"): national numbers become E.164.</summary>
    public string CountryCode { get; set; } = "49";

    /// <summary>The area code without the leading zero ("30"): a local number (no zero) gets it.</summary>
    public string AreaCode { get; set; } = "";

    /// <summary>What is dialled before an outside number ("0"); empty: a number that is not an extension goes out as it is.</summary>
    public string OutsideLinePrefix { get; set; } = "0";

    /// <summary>The feature code for the own mailbox ("*86").</summary>
    public string MailboxCode { get; set; } = "*86";

    /// <summary>The feature code that toggles "Nicht stören" ("*78").</summary>
    public string DoNotDisturbCode { get; set; } = "*78";

    /// <summary>The feature code for logging in or out of a group: the code and the group's internal number ("*85" + "300").</summary>
    public string GroupToggleCode { get; set; } = "*85";

    /// <summary>How long a SIP phone may stay registered at most, in seconds; it renews before.</summary>
    public int MaxRegisterSeconds { get; set; } = 600;

    /// <summary>How long a user's devices ring by default before the no-answer forwarding.</summary>
    public int DefaultRingSeconds { get; set; } = 20;

    /// <summary>Every SIP message goes into the telephony log (troubleshooting only: it is a lot).</summary>
    public bool SipTrace { get; set; }
}

public enum MailSecurity
{
    /// <summary>STARTTLS when the server offers it (port 587).</summary>
    Auto,
    StartTls,
    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect,
    /// <summary>No encryption (only inside a trusted network).</summary>
    None,
}

/// <summary>The mail server that sends the messages of the mailboxes, and their text. <c>{Mailbox}</c>, <c>{Caller}</c>, <c>{Date}</c>, <c>{Duration}</c> and <c>{Company}</c> are filled in.</summary>
public sealed class MailConfig
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public MailSecurity Security { get; set; } = MailSecurity.Auto;

    public string UserName { get; set; } = "";

    public string PasswordProtected { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "";

    public string VoicemailSubject { get; set; } = "Neue Sprachnachricht von {Caller}";

    public string VoicemailBody { get; set; } = "Guten Tag,\n\nin der Mailbox {Mailbox} ist eine neue Nachricht von {Caller} eingegangen ({Date}, {Duration}).\n\n{Company}";

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class DatabaseConfig
{
    public string Host { get; set; } = "db";

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "matphone";

    public string Username { get; set; } = "postgres";

    public string Password { get; set; } = "matphone";
}

public sealed class ServerConfig
{
    /// <summary>The address people use ("https://telefon.example.de"). Empty: taken from the request.</summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>"Europe/Berlin": the time zone of the installation, for dates, schedules and the time conditions of the rules.</summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>The cookie is marked secure when the site is reached over HTTPS (also behind a reverse proxy that says so).</summary>
    public bool ForceSecureCookie { get; set; }
}

public enum StorageMode
{
    /// <summary>The files are kept in the data volume.</summary>
    Local,
    /// <summary>The files are kept on a share of a NAS (SMB).</summary>
    Smb,
}

/// <summary>Where the recordings of the mailboxes and their greetings are kept.</summary>
public sealed class StorageConfig
{
    public StorageMode Mode { get; set; } = StorageMode.Local;

    public string Host { get; set; } = "";

    public int Port { get; set; } = 445;

    public string Share { get; set; } = "";

    /// <summary>A folder inside the share.</summary>
    public string Path { get; set; } = "MatPhone";

    public string Domain { get; set; } = "";

    public string UserName { get; set; } = "";

    public string PasswordProtected { get; set; } = "";
}

public sealed class SecurityConfig
{
    public int SessionDays { get; set; } = 14;

    /// <summary>Administrators have to set up an authenticator app.</summary>
    public bool RequireTotpForAdmins { get; set; }

    /// <summary>A SIP address that fails the authentication this often in ten minutes is ignored for an hour.</summary>
    public int SipMaxFailures { get; set; } = 10;

    [JsonIgnore]
    public int MaxFailedLogins => 5;
}

public sealed class BackupConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>The scratch folder of a backup; empty: {data}/tmp.</summary>
    public string TempDirectory { get; set; } = "";
}

/// <summary>How long the logs are kept, in days ("Log-Management"); the maintenance service prunes them every night.</summary>
public sealed class LogConfig
{
    public int CallDays { get; set; } = 365;

    public int TelephonyDays { get; set; } = 30;

    public int ActivityDays { get; set; } = 365;

    /// <summary>Messages of the mailboxes that were listened to are deleted after this many days (0: never).</summary>
    public int HeardMessageDays { get; set; }
}
