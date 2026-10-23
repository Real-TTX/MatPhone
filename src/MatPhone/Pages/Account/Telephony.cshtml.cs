using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Account;

/// <summary>
/// "Meine Telefonie": the forwarding and "Nicht stören", the number shown, the groups to log into, the personal function keys, the PIN and the greeting of the
/// own mailbox. An administrator opens it for somebody else with ?user=5.
/// </summary>
public sealed class TelephonyModel(MpDbContext db, ConfigStore config, FileStoreService files) : PageModelBase
{
    [BindProperty(SupportsGet = true, Name = "user")]
    public long? ForUser { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public CallSettingsInput Input { get; set; } = new();

    [BindProperty]
    public List<FunctionKeyInput> Keys { get; set; } = [];

    public long UserId => ForUser ?? Me.Me;

    public List<GroupMembership> Memberships { get; private set; } = [];

    public List<KeyView> TemplateKeys { get; private set; } = [];

    public Mailbox? Mailbox { get; private set; }

    public List<SelectListItem> OutboundNumbers { get; private set; } = [];

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Groups { get; private set; } = [];

    public string MailboxCode => config.Current.Telephony.MailboxCode;

    public string GroupCode => config.Current.Telephony.GroupToggleCode;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await Business.Users.LoadAsync(db, Me, UserId, HttpContext.RequestAborted);
        if (user is null)
        {
            return NotFound();
        }

        Input = Business.Users.ToCallSettings(user);
        Keys = await KeyTemplates.PersonalAsync(db, Me, UserId, HttpContext.RequestAborted);
        await PrepareAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostCallsAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Users.SaveCallSettingsAsync(db, Me, UserId, Input, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["MyTelephony.Saved"]);
                return RedirectToPage(new { user = ForUser, tab = "forward" });
            }

            AddErrors(result);
        }

        Tab = "forward";
        return await PageAgainAsync();
    }

    public async Task<IActionResult> OnPostGroupAsync(long groupId, bool on)
    {
        await CallGroups.SetLoggedInAsync(db, Me, UserId, groupId, on, HttpContext.RequestAborted);
        Flash(on ? L["MyTelephony.Group.LoggedIn"] : L["MyTelephony.Group.LoggedOut"]);
        return RedirectToPage(new { user = ForUser, tab = "groups" });
    }

    public async Task<IActionResult> OnPostKeysAsync()
    {
        var result = await KeyTemplates.SavePersonalAsync(db, Me, UserId, Keys, HttpContext.RequestAborted);
        if (result.Ok)
        {
            Flash(L["MyTelephony.Keys.Saved"]);
            return RedirectToPage(new { user = ForUser, tab = "keys" });
        }

        AddErrors(result, "Keys");
        Tab = "keys";
        return await PageAgainAsync(keepKeys: true);
    }

    public async Task<IActionResult> OnPostPinAsync(string? pin)
    {
        var box = await Business.Mailboxes.OwnAsync(db, Me, UserId, HttpContext.RequestAborted);
        if (box is not null)
        {
            var problem = await Business.Mailboxes.SetPinAsync(db, Me, box.Id, pin ?? "", HttpContext.RequestAborted);
            if (problem is null)
            {
                Flash(L["MyTelephony.Pin.Saved"]);
            }
            else
            {
                FlashError(problem);
            }
        }

        return RedirectToPage(new { user = ForUser, tab = "mailbox" });
    }

    /// <summary>The greeting as a WAV file, or (remove) back to the tone (the recording in the browser posts to /greeting/{id} instead).</summary>
    public async Task<IActionResult> OnPostGreetingAsync(long mailboxId, IFormFile? greeting, bool remove)
    {
        if (remove)
        {
            await Business.Mailboxes.RemoveGreetingAsync(db, Me, mailboxId, HttpContext.RequestAborted);
            Flash(L["Mailboxes.Greeting.Removed"]);
        }
        else if (greeting is { Length: > 0 and <= 10 * 1024 * 1024 })
        {
            using var stream = new MemoryStream();
            await greeting.CopyToAsync(stream, HttpContext.RequestAborted);
            var problem = await Business.Mailboxes.SetGreetingAsync(db, Me, mailboxId, stream.ToArray(), files, HttpContext.RequestAborted);
            if (problem is null)
            {
                Flash(L["Mailboxes.Greeting.Saved"]);
            }
            else
            {
                FlashError(problem);
            }
        }
        else
        {
            FlashError(L["Mailboxes.Greeting.NotWav"]);
        }

        return RedirectToPage(new { user = ForUser, tab = "mailbox" });
    }

    private async Task<IActionResult> PageAgainAsync(bool keepKeys = false)
    {
        var user = await Business.Users.LoadAsync(db, Me, UserId, HttpContext.RequestAborted);
        if (user is null)
        {
            return NotFound();
        }

        if (!keepKeys)
        {
            Keys = await KeyTemplates.PersonalAsync(db, Me, UserId, HttpContext.RequestAborted);
        }

        await PrepareAsync(user);
        return Page();
    }

    private async Task PrepareAsync(User user)
    {
        var ct = HttpContext.RequestAborted;
        Memberships = await CallGroups.MembershipsAsync(db, Me, UserId, ct);
        TemplateKeys = [.. (await KeyTemplates.EffectiveAsync(db, Me, UserId, ct: ct)).Where(k => !k.Personal)];
        Mailbox = await Business.Mailboxes.OwnAsync(db, Me, UserId, ct);
        OutboundNumbers = Controls.Choices.Of(await Lookups.OutboundNumbersAsync(db, Me, UserId, ct));
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Groups = Controls.Choices.Of(await Lookups.GroupsAsync(db, Me, ct));
        var title = ForUser is null ? L["Shell.Nav.MySettings"].Value : L["MyTelephony.For", user.DisplayName].Value;
        Head(title, L["MyTelephony.Sub"], (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Account/Telephony";
    }
}
