using MatPhone.Data;
using MatPhone.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

/// <summary>The numbers of the overview: the person's own calls and messages, and for administrators how many of what there is.</summary>
public static class Dashboard
{
    public sealed record Mine(int MissedToday, int CallsToday, int NewMessages, IReadOnlyList<CallView> Recent, IReadOnlyList<MessageView> Messages);

    public sealed record Setup(int Users, int Numbers, int Devices, int Lines, int Groups);

    public static async Task<Mine> MineAsync(MpDbContext db, Actor actor, DateTime todayUtc, CancellationToken ct = default)
    {
        actor.RequireSignedIn();
        var calls = CallLog.Mine(db, actor);
        var missed = await calls.CountAsync(c => !c.IsCaller && c.StartDate >= todayUtc && (c.Result == CallResult.Missed || c.Result == CallResult.Voicemail), ct);
        var today = await calls.CountAsync(c => c.StartDate >= todayUtc, ct);
        var recent = await calls.OrderByDescending(c => c.StartDate).Take(8).ToListAsync(ct);
        var messages = Mailboxes.Messages(db, actor);
        var unheard = await messages.CountAsync(m => m.IsNew, ct);
        var latest = await messages.Where(m => m.IsNew).OrderByDescending(m => m.ReceivedDate).Take(5).ToListAsync(ct);
        return new Mine(missed, today, unheard, recent, latest);
    }

    /// <summary>How far the installation is set up (the first steps of a new installation are shown until they are done).</summary>
    public static async Task<Setup> SetupAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return new Setup(await db.User.CountAsync(ct), await db.PhoneNumber.CountAsync(ct), await db.Device.CountAsync(ct), await db.Line.CountAsync(l => l.IsEnabled, ct), await db.CallGroup.CountAsync(ct));
    }

    public static async Task<int> DeviceCountAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return await db.Device.CountAsync(d => d.IsEnabled, ct);
    }
}
