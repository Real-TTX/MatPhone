using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.Services.Auth;
using MatPhone.Telephony;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Users;

/// <summary>One person: the account (name, address, role, password), the telephony (extension, number shown, keys, web client) and the sign-in.</summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, SignInService signIn, TwoFactorService twoFactor, SessionCache cache, ICurrentUser current,
    IPbxStatus pbx, ActivityLogger log, TimeProvider clock) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public UserInput Input { get; set; } = new();

    public User? Account { get; private set; }

    public bool IsMe => Account is not null && Account.Id == current.UserId;

    public bool HasTotp => Account?.Totp is { IsConfirmed: true };

    public bool IsLocked => Account is { } a && a.LockoutEndDate > clock.GetUtcNow().UtcDateTime;

    public int Sessions { get; private set; }

    public PresenceState State { get; private set; }

    public List<PhoneNumber> Numbers { get; private set; } = [];

    public List<Device> Devices { get; private set; } = [];

    public Mailbox? Mailbox { get; private set; }

    public List<SelectListItem> OutboundNumbers { get; private set; } = [];

    public List<SelectListItem> Templates { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (Account is { } a)
        {
            Input = await Business.Users.ToInputAsync(db, a, HttpContext.RequestAborted);
        }
        else
        {
            Input.Extension = await PhoneNumbers.NextFreeExtensionAsync(db, Me, 200, HttpContext.RequestAborted);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await Business.Users.SaveAsync(db, Me, Id, Input, PhoneNumbers.Plan(config.Current.Telephony), signIn, cache, log, clock.GetUtcNow().UtcDateTime,
            HttpContext.RequestAborted);
        if (!result.Ok)
        {
            AddErrors(result);
            return Page();
        }

        Flash(L[Id is null ? "Admin.Users.Created" : "Admin.Users.Saved"]);
        return RedirectToPage("/Admin/Users/Edit", new { id = result.Id, tab = Tab });
    }

    public async Task<IActionResult> OnPostResetTotpAsync()
    {
        if (Id is { } id)
        {
            await Business.Users.ResetTotpAsync(db, Me, id, twoFactor, log, HttpContext.RequestAborted);
            Flash(L["Admin.Users.TotpReset"]);
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostEndSessionsAsync()
    {
        if (Id is not { } id)
        {
            return NotFound();
        }

        // An administrator who ends their own sessions would lock themselves out in the middle of the click; keep the one in use.
        var keep = id == current.UserId && Guid.TryParse(User.FindFirst(AppClaims.Session)?.Value, out var token) ? token : (Guid?)null;
        await Business.Users.EndSessionsAsync(Me, id, keep, signIn, log, HttpContext.RequestAborted);
        Flash(L["Admin.Users.SessionsEnded"]);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnlockAsync()
    {
        if (Id is { } id)
        {
            await Business.Users.UnlockAsync(db, Me, id, cache, HttpContext.RequestAborted);
            Flash(L["Admin.Users.Unlocked"]);
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id && await Business.Users.DeleteAsync(db, Me, id, signIn, cache, log, HttpContext.RequestAborted) is { } problem)
        {
            FlashError(problem);
            return RedirectToPage(new { id });
        }

        Flash(L["Admin.Users.Deleted"]);
        return RedirectToPage("/Admin/Users/Index");
    }

    private async Task<bool> LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (Id is { } id)
        {
            Account = await Business.Users.LoadAsync(db, Me, id, ct);
            if (Account is null)
            {
                return false;
            }

            Sessions = await Business.Users.ActiveSessionsAsync(db, Me, id, clock.GetUtcNow().UtcDateTime, ct);
            (Numbers, Devices, Mailbox) = await Business.Users.OverviewAsync(db, Me, id, ct);
            State = pbx.PresenceOf(id);
            OutboundNumbers = Controls.Choices.Of(await Lookups.OutboundNumbersAsync(db, Me, id, ct));
        }

        Templates = Controls.Choices.Of(await Lookups.KeyTemplatesAsync(db, Me, ct));
        var title = Id is null ? L["Admin.Users.New"].Value : Account!.DisplayName;
        Head(title, null, (L["Admin.Settings"], null), (L["Admin.Users.Title"], "/Admin/Users"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Users";
        return true;
    }
}
