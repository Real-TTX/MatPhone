using System.Threading.Channels;
using MatPhone.Data;
using MatPhone.Data.Entities;

namespace MatPhone.Telephony;

/// <summary>
/// Writes the telephony log ("Telefonie-Log") in the background: the engine reports from its own threads and must never wait for the database, so the entries
/// go into a queue and are saved in batches. A full queue drops the oldest entries (a flood of refused requests must not eat the memory).
/// </summary>
public sealed class TelephonyLogWriter(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TelephonyLogWriter> logger) : BackgroundService
{
    private readonly Channel<TelephonyLog> _queue = Channel.CreateBounded<TelephonyLog>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Write(LogSeverity level, LogCategory category, string message, string? details = null, string? remote = null, long? lineId = null, long? deviceId = null, long? userId = null)
    {
        var entry = new TelephonyLog
        {
            OccurredDate = clock.GetUtcNow().UtcDateTime,
            Level = level,
            Category = category,
            Message = message.Length > 500 ? message[..500] : message,
            Details = details is { Length: > 8000 } ? details[..8000] : details,
            RemoteAddress = remote,
            LineId = lineId,
            DeviceId = deviceId,
            UserId = userId,
        };
        _queue.Writer.TryWrite(entry);
        var logLevel = level switch
        {
            LogSeverity.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogSeverity.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            _ => Microsoft.Extensions.Logging.LogLevel.Information,
        };
        logger.Log(logLevel, "[{Category}] {Message} {Remote}", category, entry.Message, remote);
    }

    public void Info(LogCategory category, string message, string? details = null, string? remote = null, long? lineId = null, long? deviceId = null, long? userId = null)
        => Write(LogSeverity.Info, category, message, details, remote, lineId, deviceId, userId);

    public void Warning(LogCategory category, string message, string? details = null, string? remote = null, long? lineId = null, long? deviceId = null, long? userId = null)
        => Write(LogSeverity.Warning, category, message, details, remote, lineId, deviceId, userId);

    public void Error(LogCategory category, string message, string? details = null, string? remote = null, long? lineId = null, long? deviceId = null, long? userId = null)
        => Write(LogSeverity.Error, category, message, details, remote, lineId, deviceId, userId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TelephonyLog>(100);
        while (await _queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            batch.Clear();
            while (batch.Count < 100 && _queue.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MpDbContext>();
                db.TelephonyLog.AddRange(batch);
                await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The telephony log could not be written ({Count} entries lost).", batch.Count);
            }
        }
    }
}
