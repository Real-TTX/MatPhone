using System.Text.Json;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Telephony;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages;

/// <summary>
/// The web client: the phone of a person in the browser. The page carries what it needs to start (the person, the keys, the colleagues, the teams, the last
/// calls) as a JSON block; phone.js connects to /ws/phone and does the rest (signalling, WebRTC, the busy lamps).
/// </summary>
public sealed class PhoneModel(MpDbContext db, ConfigStore config, IPbxStatus pbx) : PageModelBase
{
    public string Data { get; private set; } = "{}";

    public bool Allowed { get; private set; } = true;

    public string Prefix => config.Current.Telephony.OutsideLinePrefix;

    /// <summary>A number to dial at once (from a link "call back" of another page): ?dial=0301234.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Dial { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;
        var userId = me.Me;
        Head(L["Phone.Title"], null, (L["Phone.Title"], null));
        ViewData["Width"] = "wide";
        ViewData["OwnHead"] = true;

        var user = await Business.Users.LoadAsync(db, me, userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        Allowed = user.WebClientEnabled;
        var plan = PhoneNumbers.Plan(config.Current.Telephony);
        var keys = await KeyTemplates.EffectiveAsync(db, me, userId, ct: ct);
        var colleagues = await Business.Users.DirectoryAsync(db, me, ct);
        var teams = await Business.Teams.ListAsync(db, me, ct);
        var groups = await CallGroups.MembershipsAsync(db, me, userId, ct);
        var recent = await Business.CallLog.RecentAsync(db, me, 12, ct);
        var presence = pbx.PresenceAll();
        var extension = await Business.Users.PrimaryExtensionAsync(db, userId, ct);

        Data = JsonSerializer.Serialize(new
        {
            me = new { id = userId, name = user.DisplayName, extension, dnd = user.DoNotDisturb, forward = user.ForwardAlways },
            dial = Dial,
            prefix = config.Current.Telephony.OutsideLinePrefix,
            mailboxCode = config.Current.Telephony.MailboxCode,
            keys = keys.Select(k => new { kind = k.Kind.ToString(), label = k.Label, user = k.TargetUserId, group = k.TargetGroupId, number = k.Number }),
            colleagues = colleagues.Where(c => c.Id != userId).Select(c => new
            {
                id = c.Id, name = c.Name, extension = c.Extension, teams = c.TeamIds,
                state = Presence.Key(presence.TryGetValue(c.Id, out var s) ? s : PresenceState.Offline),
            }),
            teams = teams.Select(t => new { id = t.Id, name = t.Name, color = t.Color }),
            groups = groups.Select(g => new { id = g.GroupId, name = g.GroupName, number = g.Number, on = g.IsLoggedIn }),
            recent = recent.Select(c => new
            {
                when = c.StartDate,
                incoming = !c.IsCaller,
                result = c.Result.ToString(),
                number = c.IsCaller ? c.CalledNumber : c.CallerNumber,
                shown = NumberRules.Display(c.IsCaller ? c.CalledNumber : c.CallerNumber, plan),
                name = c.IsCaller ? null : c.CallerName,
                label = Labels.Of(c.Result),
            }),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Page();
    }
}
