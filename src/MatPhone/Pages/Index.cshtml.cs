using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Services;
using MatPhone.Telephony;

namespace MatPhone.Pages;

/// <summary>The overview: the own calls and messages of today; for administrators also how the telephony stands and what is still to set up.</summary>
public sealed class IndexModel(MpDbContext db, ConfigStore config, IPbxStatus pbx, ICurrentUser current, TimeProvider clock) : PageModelBase
{
    public Dashboard.Mine Mine { get; private set; } = null!;

    public Dashboard.Setup? Setup { get; private set; }

    public IPbxStatus Pbx => pbx;

    public bool IsAdmin => current.IsAdmin;

    public PresenceState MyState { get; private set; }

    public string? Extension { get; private set; }

    public string Greeting { get; private set; } = "";

    public bool PublicAddressMissing => string.IsNullOrWhiteSpace(config.Current.Telephony.PublicAddress);

    public string Show(string? number) => NumberRules.Display(number, PhoneNumbers.Plan(config.Current.Telephony));

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var now = clock.GetUtcNow().UtcDateTime;
        var hour = Fmt.Local(now).Hour;
        var hello = L[hour < 11 ? "Dashboard.Greeting.Morning" : hour < 18 ? "Dashboard.Greeting.Day" : "Dashboard.Greeting.Evening"].Value;
        Greeting = string.Format(Fmt.Culture, hello, (current.Name ?? "").Split(' ')[0]);
        Head(L["Dashboard.Title"], Greeting, (L["Dashboard.Title"], null));
        Mine = await Dashboard.MineAsync(db, Me, Fmt.UtcFromLocal(Fmt.Local(now).Date), ct);
        MyState = pbx.PresenceOf(Me.Me);
        Extension = await Business.Users.PrimaryExtensionAsync(db, Me.Me, ct);
        if (current.IsAdmin)
        {
            Setup = await Dashboard.SetupAsync(db, Me, ct);
        }
    }
}
