namespace MatPhone.Data.Entities;

/// <summary>The simple role system: administrator and user. Anonymous is a share link, not a user.</summary>
public enum UserRole
{
    Admin,
    User,
}

public class User : AuditedEntity, ISoftDeletable
{
    /// <summary>The sign-in name (lower case).</summary>
    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    public string? Phone { get; set; }

    /// <summary>"system", "light" or "dark": kept per user, applied at sign-in.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"en" or "de": the language of the interface for this person.</summary>
    public string Language { get; set; } = "de";

    /// <summary>The person may phone in the browser (the web client is a device of its own).</summary>
    public bool WebClientEnabled { get; set; } = true;

    /// <summary>"Nicht stören": no call rings; callers go to the fallback (mailbox) at once.</summary>
    public bool DoNotDisturb { get; set; }

    /// <summary>Every call goes to this number at once (internal or external); empty: no forwarding.</summary>
    public string? ForwardAlways { get; set; }

    /// <summary>When the person is on the phone already.</summary>
    public string? ForwardBusy { get; set; }

    /// <summary>When nobody answers within <see cref="NoAnswerSeconds"/>; empty: the mailbox of the person, if there is one.</summary>
    public string? ForwardNoAnswer { get; set; }

    public int NoAnswerSeconds { get; set; } = 20;

    /// <summary>The number shown to the called party on a call out (an external number of the person); null: the default number of the line.</summary>
    public long? OutboundNumberId { get; set; }

    /// <summary>The function keys of the person (its devices may have their own).</summary>
    public long? KeyTemplateId { get; set; }

    /// <summary>A call that nobody took goes to the mailbox of the person.</summary>
    public bool MailboxOnNoAnswer { get; set; } = true;

    public DateTime? LastLoginDate { get; set; }

    public int FailedLogins { get; set; }

    public DateTime? LockoutEndDate { get; set; }

    public DateTime? PasswordChangedDate { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public UserTotp? Totp { get; set; }

    /// <summary>The initials for the round avatar: two letters of the display name.</summary>
    public string Initials => Services.Fmt.Initials(DisplayName);
}

/// <summary>The second factor of a user (RFC 6238). It counts only once <see cref="IsConfirmed"/> is set by a valid first code.</summary>
public class UserTotp : AuditedEntity
{
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The shared secret, protected with the key ring of the application.</summary>
    public string Secret { get; set; } = "";

    public bool IsConfirmed { get; set; }

    /// <summary>The time step of the last accepted code: a code works once.</summary>
    public long LastUsedStep { get; set; }

    public List<UserRecoveryCode> RecoveryCodes { get; set; } = [];
}

public class UserRecoveryCode : AuditedEntity
{
    public long UserTotpId { get; set; }

    public UserTotp? UserTotp { get; set; }

    public string CodeHash { get; set; } = "";

    public DateTime? UsedDate { get; set; }
}

/// <summary>A signed-in browser. The cookie carries only <see cref="Token"/>; the session is valid as long as this row is.</summary>
public class UserSession : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime ExpiresDate { get; set; }

    public DateTime LastSeenDate { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? RevokedDate { get; set; }
}
