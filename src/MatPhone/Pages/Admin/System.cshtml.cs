using System.Diagnostics;
using System.Runtime.InteropServices;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Services;
using MatPhone.Services.Backup;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin;

/// <summary>What the installation is made of and how it is doing: version, runtime, data volume, database, and the tables that are cleaned up by the maintenance.</summary>
public sealed class SystemModel(SystemService system, ConfigStore config, ActivityLogger log, MpDbContext db, SecretProtector secrets, ICurrentUser me) : PageModelBase
{
    public sealed record Folder(string Name, string Hint, long Bytes, int Files);

    public string Version => AppVersion.Current;

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string Os => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.OSArchitecture.ToString();

    public DateTime Started => Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public long Memory => Process.GetCurrentProcess().WorkingSet64;

    public string DataDir => config.DataDir;

    public bool CanCreateDemo { get; private set; }

    public string? DatabaseVersion { get; private set; }

    public long DatabaseBytes { get; private set; }

    public List<Folder> Folders { get; private set; } = [];

    public Dictionary<string, int> Counts { get; private set; } = [];

    public string TimeZone => Fmt.Zone.Id;

    public StorageMode Storage => config.Current.Storage.Mode;

    public bool BackupBusy => BackupService.IsBusy;

    public async Task OnGetAsync()
    {
        Head(L["Admin.System.Title"], L["Admin.System.Sub"], (L["Admin.Settings"], null), (L["Admin.System.Title"], null));
        var ct = HttpContext.RequestAborted;

        var database = await system.GetDatabaseAsync(ct);
        DatabaseVersion = database.Version;
        DatabaseBytes = database.Bytes;
        Counts = await system.CountRowsAsync(ct);
        CanCreateDemo = await Demo.IsEmptyAsync(db, Actor.From(me), ct);

        Folders =
        [
            Measure("files", L["Admin.System.Folder.Files.Name"], L["Admin.System.Folder.Files.Hint"]),
            Measure("keys", L["Admin.System.Folder.Keys.Name"], L["Admin.System.Folder.Keys.Hint"]),
            Measure("config", L["Admin.System.Folder.Config.Name"], "app.json"),
            Measure("backups", L["Admin.System.Folder.Backups.Name"], L["Admin.System.Folder.Backups.Hint"]),
            Measure("tmp", L["Admin.System.Folder.Tmp.Name"], L["Admin.System.Folder.Tmp.Hint"]),
        ];
    }

    /// <summary>Removes old sessions, used codes and old logs now (the maintenance does the same once a day).</summary>
    public async Task<IActionResult> OnPostMaintainAsync()
    {
        var result = await system.MaintainAsync(HttpContext.RequestAborted);
        await log.LogAsync(L["Admin.System.Log.Cleanup"], "System", null, L["Admin.System.Log.CleanupSummary", result.Total].Value);
        Flash(result.Total switch
        {
            0 => L["Admin.System.Cleanup.Nothing"],
            1 => L["Admin.System.Cleanup.One", result.Total],
            _ => L["Admin.System.Cleanup.Many", result.Total],
        });
        return RedirectToPage();
    }

    /// <summary>Writes sample data into an empty installation (for trying the software out).</summary>
    public async Task<IActionResult> OnPostDemoAsync()
    {
        if (!await Demo.CreateAsync(db, Actor.From(me), secrets, PhoneNumbers.Plan(config.Current.Telephony), HttpContext.RequestAborted))
        {
            FlashError(L["Admin.System.Demo.NotEmpty"]);
            return RedirectToPage();
        }

        await log.LogAsync(L["Admin.System.Log.Demo"], "System");
        Flash(L["Admin.System.Demo.Created"]);
        return RedirectToPage("/Index");
    }

    private Folder Measure(string name, string title, string hint)
    {
        var path = Path.Combine(config.DataDir, name);
        if (!Directory.Exists(path))
        {
            return new Folder(title, hint, 0, 0);
        }

        long bytes = 0;
        var count = 0;
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            bytes += f.Length;
            count++;
        }

        return new Folder(title, hint, bytes, count);
    }
}
