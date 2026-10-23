using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.Telephony;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin.Lines;

/// <summary>
/// One line: where the provider is, the account, how numbers and the shown number are written. A new line starts from a provider profile: the page first
/// lists the profiles, the chosen one fills the form.
/// </summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, SecretProtector secrets, IPbxStatus pbx, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public LineInput Input { get; set; } = new();

    public bool ChooseProfile { get; private set; }

    public List<LineProfile> Profiles { get; private set; } = [];

    public LineProfile? Profile { get; private set; }

    public List<PhoneNumber> Numbers { get; private set; } = [];

    public LineStatus? Status { get; private set; }

    public async Task<IActionResult> OnGetAsync(long? profile)
    {
        var ct = HttpContext.RequestAborted;
        if (Id is { } id)
        {
            var line = await Business.Lines.LoadAsync(db, Me, id, ct);
            if (line is null)
            {
                return NotFound();
            }

            Input = Business.Lines.ToInput(line);
        }
        else if (profile is { } profileId && await Business.Lines.LoadProfileAsync(db, Me, profileId, ct) is { } chosen)
        {
            Input = Business.Lines.FromProfile(chosen);
        }
        else
        {
            ChooseProfile = true;
            Profiles = await Business.Lines.ProfileListAsync(db, Me, ct);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Lines.SaveAsync(db, Me, Id, Input, PhoneNumbers.Plan(config.Current.Telephony), secrets, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Lines.Saved"]);
                return RedirectToPage(new { id = result.Id, tab = Tab });
            }

            AddErrors(result);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id && await Business.Lines.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted) is { } problem)
        {
            FlashError(problem);
            return RedirectToPage(new { id });
        }

        Flash(L["Lines.Deleted"]);
        return RedirectToPage("/Admin/Lines/Index");
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (Input.ProfileId is { } profileId)
        {
            Profile = await Business.Lines.LoadProfileAsync(db, Me, profileId, ct);
        }

        if (Id is { } id)
        {
            Numbers = await Business.Lines.NumbersAsync(db, Me, id, ct);
            Status = pbx.Lines.FirstOrDefault(l => l.LineId == id);
        }

        var title = Id is null ? L["Lines.New"].Value : Input.Name;
        Head(title, ChooseProfile ? L["Lines.ChooseProfile"].Value : null, (L["Admin.Settings"], null), (L["Lines.Title"], "/Admin/Lines"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Lines";
    }
}
