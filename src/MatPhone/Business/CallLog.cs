using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

/// <summary>A call as the call list of a person shows it: the other party is the one that matters to the person.</summary>
public sealed record CallView
{
    public long Id { get; init; }

    public CallDirection Direction { get; init; }

    public CallResult Result { get; init; }

    public DateTime StartDate { get; init; }

    public int DurationSeconds { get; init; }

    public string? CallerNumber { get; init; }

    public string? CallerName { get; init; }

    public string? CalledNumber { get; init; }

    public bool IsCaller { get; init; }

    public bool Answered { get; init; }
}

/// <summary>The log of the calls ("Anrufprotokoll"): every person sees the own calls, administrators all of them. The engine writes it as the system.</summary>
public static class CallLog
{
    public static IQueryable<CallRecord> All(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.CallRecord.AsNoTracking();
    }

    public static IQueryable<CallRecord> Search(IQueryable<CallRecord> query, string text)
    {
        var like = Like.Contains(text);
        var digits = Core.Telephony.NumberRules.Clean(text).TrimStart('+', '0');
        var digitsLike = digits.Length > 0 ? Like.Contains(digits) : like;
        return query.Where(c => (c.CallerNumber != null && EF.Functions.ILike(c.CallerNumber, digitsLike)) || (c.CalledNumber != null && EF.Functions.ILike(c.CalledNumber, digitsLike))
            || (c.CallerName != null && EF.Functions.ILike(c.CallerName, like)));
    }

    /// <summary>The calls of a person (the person called, or it rang for the person).</summary>
    public static IQueryable<CallView> Mine(MpDbContext db, Actor actor)
    {
        var me = actor.Me;
        return db.CallParticipant.AsNoTracking().Where(p => p.UserId == me)
            .Select(p => new CallView
            {
                Id = p.CallRecordId,
                Direction = p.CallRecord!.Direction,
                Result = p.CallRecord.Result,
                StartDate = p.CallRecord.StartDate,
                DurationSeconds = p.CallRecord.DurationSeconds,
                CallerNumber = p.CallRecord.CallerNumber,
                CallerName = p.CallRecord.CallerName,
                CalledNumber = p.CallRecord.CalledNumber,
                IsCaller = p.IsCaller,
                Answered = p.Answered,
            });
    }

    public static IQueryable<CallView> SearchMine(IQueryable<CallView> query, string text)
    {
        var like = Like.Contains(text);
        var digits = Core.Telephony.NumberRules.Clean(text).TrimStart('+', '0');
        var digitsLike = digits.Length > 0 ? Like.Contains(digits) : like;
        return query.Where(c => (c.CallerNumber != null && EF.Functions.ILike(c.CallerNumber, digitsLike)) || (c.CalledNumber != null && EF.Functions.ILike(c.CalledNumber, digitsLike))
            || (c.CallerName != null && EF.Functions.ILike(c.CallerName, like)));
    }

    /// <summary>The kinds of the own call list: missed (it rang for me and I did not take it), in, out.</summary>
    public static IQueryable<CallView> FilterMine(IQueryable<CallView> query, string kind) => kind switch
    {
        "missed" => query.Where(c => !c.IsCaller && !c.Answered),
        "in" => query.Where(c => !c.IsCaller),
        _ => query.Where(c => c.IsCaller),
    };

    /// <summary>The last calls of a person, newest first.</summary>
    public static async Task<List<CallView>> RecentAsync(MpDbContext db, Actor actor, int count, CancellationToken ct = default)
        => await Mine(db, actor).OrderByDescending(c => c.StartDate).Take(count).ToListAsync(ct);

    /// <summary>Calls the person did not take since <paramref name="since"/> (another member of a group took it: not missed).</summary>
    public static Task<int> MissedSinceAsync(MpDbContext db, Actor actor, DateTime since, CancellationToken ct = default)
        => Mine(db, actor).CountAsync(c => !c.IsCaller && c.StartDate >= since && (c.Result == CallResult.Missed || c.Result == CallResult.Voicemail), ct);

    public static async Task<CallRecord?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.CallRecord.AsNoTracking().Include(c => c.Participants).ThenInclude(p => p.User).FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    /// <summary>Writes a finished call (the engine, as the system).</summary>
    public static async Task WriteAsync(MpDbContext db, CallRecord record, CancellationToken ct = default)
    {
        db.CallRecord.Add(record);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>How many entries the logs hold (the page of the log management shows it next to the days).</summary>
public static class LogCounts
{
    public sealed record Counts(int Calls, int Telephony, int Activity);

    public static async Task<Counts> OfAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return new Counts(await db.CallRecord.CountAsync(ct), await db.TelephonyLog.CountAsync(ct), await db.ActivityLog.CountAsync(ct));
    }
}

/// <summary>What the engine reports ("Telefonie-Log"): only administrators read it; the engine writes it as the system.</summary>
public static class TelephonyLogs
{
    public static IQueryable<TelephonyLog> Query(MpDbContext db, Actor actor)
    {
        actor.RequireAdmin();
        return db.TelephonyLog.AsNoTracking();
    }

    public static IQueryable<TelephonyLog> Search(IQueryable<TelephonyLog> query, string text)
    {
        var like = Like.Contains(text);
        return query.Where(l => EF.Functions.ILike(l.Message, like) || (l.Details != null && EF.Functions.ILike(l.Details, like)) || (l.RemoteAddress != null && EF.Functions.ILike(l.RemoteAddress, like)));
    }

    public static async Task<TelephonyLog?> LoadAsync(MpDbContext db, Actor actor, long id, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.TelephonyLog.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
    }
}
