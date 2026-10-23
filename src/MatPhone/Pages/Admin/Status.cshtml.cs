using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Telephony;

namespace MatPhone.Pages.Admin;

/// <summary>How the telephony is doing right now: the engine, the registered phones, the lines, the calls in progress. The page refreshes itself.</summary>
public sealed class StatusModel(IPbxStatus pbx, ConfigStore config, MpDbContext db) : PageModelBase
{
    public IPbxStatus Pbx => pbx;

    public TelephonyConfig Settings => config.Current.Telephony;

    public int DeviceCount { get; private set; }

    public string? Problem => pbx.Problem switch
    {
        null => null,
        "no-public-address" => L["Telephony.PublicAddress.Missing"],
        var other => other,
    };

    public async Task OnGetAsync()
    {
        Head(L["Status.Title"], L["Status.Sub"], (L["Admin.Settings"], null), (L["Status.Title"], null));
        DeviceCount = await Dashboard.DeviceCountAsync(db, Me, HttpContext.RequestAborted);
    }
}
