using System.Collections.Concurrent;

namespace MatPhone.Telephony;

/// <summary>What a busy lamp shows of a person.</summary>
public enum PresenceState
{
    Offline,
    Idle,
    DoNotDisturb,
    Ringing,
    Busy,
}

/// <summary>
/// The state of every person, worked out from the calls, the registrations, the web clients and "Nicht stören". Whoever shows it (the panel of the web client,
/// the busy lamps of the phones) listens to <see cref="StateChanged"/>.
/// </summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<long, PresenceState> _states = new();
    private Func<IReadOnlyCollection<ActiveCall>> _calls = () => [];
    private Func<long, bool> _isOnline = _ => false;
    private Func<PbxSnapshot> _snapshot = () => PbxSnapshot.Empty;

    public event Action<long, PresenceState>? StateChanged;

    public void Attach(Func<IReadOnlyCollection<ActiveCall>> calls, Func<long, bool> isOnline, Func<PbxSnapshot> snapshot)
    {
        _calls = calls;
        _isOnline = isOnline;
        _snapshot = snapshot;
    }

    public PresenceState Of(long userId) => _states.TryGetValue(userId, out var state) ? state : Compute(userId);

    public IReadOnlyDictionary<long, PresenceState> All()
        => _snapshot().Users.Keys.ToDictionary(id => id, Of);

    /// <summary>On the phone right now (answered, or dialling out).</summary>
    public bool IsBusy(long userId)
        => _calls().Any(c => !c.IsOver && ((c.A.UserId == userId && !c.A.IsEnded) || (c.B is { } b && b.UserId == userId && b.State == LegState.Answered && !b.IsEnded)));

    /// <summary>Something about the person changed: works the state out again and tells the listeners when it is another.</summary>
    public void Changed(long userId)
    {
        var state = Compute(userId);
        var previous = _states.TryGetValue(userId, out var old) ? old : (PresenceState?)null;
        _states[userId] = state;
        if (previous != state)
        {
            StateChanged?.Invoke(userId, state);
        }
    }

    /// <summary>Works out everybody again (after a reload of the configuration, and now and then for registrations that ran out).</summary>
    public void RefreshAll()
    {
        foreach (var id in _snapshot().Users.Keys)
        {
            Changed(id);
        }
    }

    private PresenceState Compute(long userId)
    {
        if (IsBusy(userId))
        {
            return PresenceState.Busy;
        }

        if (_calls().Any(c => !c.IsOver && c.Ringing.Values.Any(l => l.UserId == userId && !l.IsEnded)))
        {
            return PresenceState.Ringing;
        }

        if (_snapshot().Users.TryGetValue(userId, out var user) && user.DoNotDisturb)
        {
            return PresenceState.DoNotDisturb;
        }

        return _isOnline(userId) ? PresenceState.Idle : PresenceState.Offline;
    }
}
