namespace MatPhone.Business;

/// <summary>What changed in the configuration of the telephony, so that the engine reloads it (lines re-register, devices are read again).</summary>
public enum TelephonyChange
{
    Users,
    Devices,
    Lines,
    Numbers,
    Groups,
    Rules,
    Mailboxes,
    Keys,
    Settings,
}

/// <summary>
/// The business layer says what it changed, the engine listens (<see cref="Telephony.PbxEngine"/>). A static event, so that a static business function needs no
/// reference to the engine; handlers must be quick and never throw.
/// </summary>
public static class TelephonySignals
{
    public static event Action<TelephonyChange>? Changed;

    /// <summary>A user changed something the panels show at once (forwarding, "Nicht stören", logged into a group): the user's id.</summary>
    public static event Action<long>? UserStateChanged;

    public static void Notify(TelephonyChange change) => Changed?.Invoke(change);

    public static void NotifyUserState(long userId) => UserStateChanged?.Invoke(userId);
}
