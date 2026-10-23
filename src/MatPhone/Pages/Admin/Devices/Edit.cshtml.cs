using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.Telephony;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Devices;

/// <summary>One SIP phone: what it is, whose it is, its keys, and the account it registers with (shown for setting the phone up).</summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, SecretProtector secrets, IPbxStatus pbx, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public DeviceInput Input { get; set; } = new();

    public Device? Device { get; private set; }

    public RegistrationView? Registration { get; private set; }

    public string Password { get; private set; } = "";

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Templates { get; private set; } = [];

    public string Server => string.IsNullOrWhiteSpace(config.Current.Telephony.PublicAddress) ? Request.Host.Host : config.Current.Telephony.PublicAddress;

    public int Port => config.Current.Telephony.SipPort;

    public string MailboxCode => config.Current.Telephony.MailboxCode;

    public async Task<IActionResult> OnGetAsync(long? user)
    {
        if (Id is not null)
        {
            if (!await LoadAsync())
            {
                return NotFound();
            }

            Input = Business.Devices.ToInput(Device!);
        }
        else
        {
            Input.UserId = user;
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Devices.SaveAsync(db, Me, Id, Input, secrets, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Devices.Saved"]);
                return RedirectToPage(new { id = result.Id, tab = Id is null ? "sip" : Tab });
            }

            AddErrors(result);
        }

        await LoadAsync();
        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRenewAsync()
    {
        if (Id is { } id && await Business.Devices.RenewPasswordAsync(db, Me, id, secrets, log, HttpContext.RequestAborted) is not null)
        {
            Flash(L["Devices.Renewed"]);
        }

        return RedirectToPage(new { id = Id, tab = "sip" });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id)
        {
            await Business.Devices.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted);
            Flash(L["Devices.Deleted"]);
        }

        return RedirectToPage("/Admin/Devices/Index");
    }

    private async Task<bool> LoadAsync()
    {
        if (Id is not { } id)
        {
            return true;
        }

        Device = await Business.Devices.LoadAsync(db, Me, id, HttpContext.RequestAborted);
        if (Device is null)
        {
            return false;
        }

        Password = Business.Devices.Password(Me, Device, secrets);
        Registration = pbx.Registrations.FirstOrDefault(r => r.DeviceId == id);
        return true;
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Templates = Controls.Choices.Of(await Lookups.KeyTemplatesAsync(db, Me, ct));
        var title = Id is null ? L["Devices.New"].Value : Device?.Name ?? Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Devices.Title"], "/Admin/Devices"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Devices";
    }
}
