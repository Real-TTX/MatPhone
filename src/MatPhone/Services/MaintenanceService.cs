using MatPhone.Configuration;
using MatPhone.Data;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Services;

/// <summary>What was cleaned up: how many rows of each kind were removed.</summary>
public sealed record MaintenanceResult(int Sessions, int ActivityEntries, int BackupRuns, int Calls, int TelephonyEntries, int Messages)
{
    public int Total => Sessions + ActivityEntries + BackupRuns + Calls + TelephonyEntries + Messages;
}

/// <summary>
/// The log management of the installation: keeps the tables that only grow small, by the days of the log settings (calls, telephony log, activity, heard messages)
/// and sessions that ended long ago. Runs once a day (and on request on the system page). It deletes with single statements and never touches the configuration.
/// </summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, ConfigStore config, ILogger<MaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var result = await RunAsync(scope.ServiceProvider.GetRequiredService<MpDbContext>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                        config.Current.Logs, stoppingToken);
                    if (result.Total > 0)
                    {
                        logger.LogInformation("Maintenance removed {Total} old rows.", result.Total);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The maintenance run failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // the application is stopping
        }
    }

    public static async Task<MaintenanceResult> RunAsync(MpDbContext db, TimeProvider clock, LogConfig logs, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var month = now.AddDays(-30);
        var year = now.AddDays(-365);

        var sessions = await db.UserSession.Where(s => s.ExpiresDate < month || (s.RevokedDate != null && s.RevokedDate < month)).ExecuteDeleteAsync(ct);
        var activity = await db.ActivityLog.Where(a => a.OccurredDate < now.AddDays(-Math.Max(1, logs.ActivityDays))).ExecuteDeleteAsync(ct);
        var runs = await db.BackupRun.Where(r => r.EndDate < year).ExecuteDeleteAsync(ct);
        var calls = await db.CallRecord.Where(c => c.StartDate < now.AddDays(-Math.Max(1, logs.CallDays))).ExecuteDeleteAsync(ct);
        var telephony = await db.TelephonyLog.Where(l => l.OccurredDate < now.AddDays(-Math.Max(1, logs.TelephonyDays))).ExecuteDeleteAsync(ct);
        var messages = 0;
        if (logs.HeardMessageDays > 0)
        {
            var heardBefore = now.AddDays(-logs.HeardMessageDays);
            messages = await db.MailboxMessage.Where(m => !m.IsNew && m.ReceivedDate < heardBefore)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.UpdateState, UpdateState.Deleted), ct);
        }

        return new MaintenanceResult(sessions, activity, runs, calls, telephony, messages);
    }
}
