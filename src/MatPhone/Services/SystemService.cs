using MatPhone.Data;
using MatPhone.Localization;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Services;

/// <summary>What the database says about itself: server version and size.</summary>
public sealed record DatabaseInfo(string? Version, long Bytes);

/// <summary>What the system page shows about the database, and the maintenance it can start. The page itself never touches the database.</summary>
public sealed class SystemService(MpDbContext db, TimeProvider clock, Configuration.ConfigStore config)
{
    public async Task<DatabaseInfo> GetDatabaseAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT version()";
            var text = System.Text.RegularExpressions.Regex.Match((string?)await version.ExecuteScalarAsync(ct) ?? "", @"^PostgreSQL [\d.]+").Value;
            await using var size = connection.CreateCommand();
            size.CommandText = "SELECT pg_database_size(current_database())";
            return new DatabaseInfo(text, Convert.ToInt64(await size.ExecuteScalarAsync(ct)));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>How many rows the main tables hold, in the order the page lists them.</summary>
    public async Task<Dictionary<string, int>> CountRowsAsync(CancellationToken ct) => new()
    {
        [Text.T("System.Rows.Users")] = await db.User.CountAsync(ct),
        [Text.T("System.Rows.Devices")] = await db.Device.CountAsync(ct),
        [Text.T("System.Rows.Numbers")] = await db.PhoneNumber.CountAsync(ct),
        [Text.T("System.Rows.Groups")] = await db.CallGroup.CountAsync(ct),
        [Text.T("System.Rows.Lines")] = await db.Line.CountAsync(ct),
        [Text.T("System.Rows.Calls")] = await db.CallRecord.CountAsync(ct),
        [Text.T("System.Rows.Messages")] = await db.MailboxMessage.CountAsync(ct),
        [Text.T("System.Rows.TelephonyLog")] = await db.TelephonyLog.CountAsync(ct),
        [Text.T("System.Rows.Files")] = await db.StoredFile.CountAsync(ct),
    };

    /// <summary>Removes old sessions, run-out links and old logs now (the daily maintenance does the same).</summary>
    public Task<MaintenanceResult> MaintainAsync(CancellationToken ct) => MaintenanceService.RunAsync(db, clock, config.Current.Logs, ct);
}
