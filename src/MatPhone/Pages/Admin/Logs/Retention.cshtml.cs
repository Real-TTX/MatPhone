using System.ComponentModel.DataAnnotations;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin.Logs;

/// <summary>The log management: how long the call log, the telephony log, the activity and heard messages are kept; the cleanup runs every night or now.</summary>
public sealed class RetentionModel(ConfigStore config, MpDbContext db, SystemService system, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [Range(1, 3650, ErrorMessage = "Logs.Retention.Range")]
        public int CallDays { get; set; } = 365;

        [Range(1, 3650, ErrorMessage = "Logs.Retention.Range")]
        public int TelephonyDays { get; set; } = 30;

        [Range(1, 3650, ErrorMessage = "Logs.Retention.Range")]
        public int ActivityDays { get; set; } = 365;

        [Range(0, 3650, ErrorMessage = "Logs.Retention.Range")]
        public int HeardMessageDays { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public LogCounts.Counts Counts { get; private set; } = new(0, 0, 0);

    public async Task OnGetAsync()
    {
        var l = config.Current.Logs;
        Input = new InputModel { CallDays = l.CallDays, TelephonyDays = l.TelephonyDays, ActivityDays = l.ActivityDays, HeardMessageDays = l.HeardMessageDays };
        await PrepareAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Me.RequireAdmin();
        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return Page();
        }

        config.Update(c =>
        {
            c.Logs.CallDays = Input.CallDays;
            c.Logs.TelephonyDays = Input.TelephonyDays;
            c.Logs.ActivityDays = Input.ActivityDays;
            c.Logs.HeardMessageDays = Input.HeardMessageDays;
        });
        await log.LogAsync(L["Logs.Retention.Log.Changed"], "Logs");
        Flash(L["Logs.Retention.Saved"]);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCleanAsync()
    {
        Me.RequireAdmin();
        var result = await system.MaintainAsync(HttpContext.RequestAborted);
        await log.LogAsync(L["Admin.System.Log.Cleanup"], "System", null, L["Admin.System.Log.CleanupSummary", result.Total].Value);
        Flash(result.Total == 0 ? L["Admin.System.Cleanup.Nothing"] : L["Admin.System.Cleanup.Many", result.Total]);
        return RedirectToPage();
    }

    private async Task PrepareAsync()
    {
        Head(L["Logs.Retention.Title"], L["Logs.Retention.Sub"], (L["Admin.Settings"], null), (L["Logs.Retention.Title"], null));
        ViewData["Width"] = "narrow";
        Counts = await LogCounts.OfAsync(db, Me, HttpContext.RequestAborted);
    }
}
