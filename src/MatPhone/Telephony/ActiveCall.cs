using System.Collections.Concurrent;
using MatPhone.Data.Entities;

namespace MatPhone.Telephony;

/// <summary>
/// A call while it lasts: the caller (<see cref="A"/>), the legs that ring, the leg that took it (<see cref="B"/>) and what the log will say. One call can ring
/// many legs (a group, all devices of a person) and change its parties (a transfer); the call manager is the only one that changes it.
/// </summary>
public sealed class ActiveCall
{
    private readonly object _gate = new();
    private TaskCompletionSource<Leg?> _winner = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ActiveCall(Leg caller, CallerId callerId, CallDirection direction, DateTime start)
    {
        A = caller;
        Caller = callerId;
        Direction = direction;
        StartDate = start;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public CallDirection Direction { get; }

    public DateTime StartDate { get; }

    public DateTime? AnswerDate { get; set; }

    public CallerId Caller { get; set; }

    /// <summary>The number that was dialled or called (as the log shows it).</summary>
    public string? CalledNumber { get; set; }

    public long? LineId { get; set; }

    public long? GroupId { get; set; }

    /// <summary>The party that called, or after a transfer the party that stays.</summary>
    public Leg A { get; set; }

    /// <summary>The party that answered (null while it rings).</summary>
    public Leg? B { get; set; }

    public MediaBridge? Bridge { get; set; }

    /// <summary>The legs that ring right now (the panels show them, a pickup takes one of their calls).</summary>
    public ConcurrentDictionary<string, Leg> Ringing { get; } = new();

    /// <summary>The users it rang for (the call list of each of them gets it).</summary>
    public ConcurrentDictionary<long, bool> RungUsers { get; } = new();

    public long? AnsweredBy { get; set; }

    public CallResult? Result { get; set; }

    /// <summary>The caller hung up or the call is over: everything that waits for it stops.</summary>
    public CancellationTokenSource Ended { get; } = new();

    public bool IsOver => Ended.IsCancellationRequested;

    /// <summary>Starts a new round of ringing: the first leg that answers in it is the winner.</summary>
    public Task<Leg?> NewRound()
    {
        lock (_gate)
        {
            _winner = new TaskCompletionSource<Leg?>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _winner.Task;
        }
    }

    /// <summary>A leg answered: true when it is the first of the round (the others are cancelled), false when another was quicker.</summary>
    public bool TryWin(Leg leg)
    {
        lock (_gate)
        {
            return _winner.TrySetResult(leg);
        }
    }

    public void EndRound()
    {
        lock (_gate)
        {
            _winner.TrySetResult(null);
        }
    }

    /// <summary>What the other side of <paramref name="leg"/> is (A for B, B for A).</summary>
    public Leg? Other(Leg leg) => ReferenceEquals(leg, A) ? B : ReferenceEquals(leg, B) ? A : null;
}
