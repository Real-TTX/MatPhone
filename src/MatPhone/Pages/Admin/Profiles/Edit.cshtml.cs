using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin.Profiles;

/// <summary>One provider profile: the settings a new line of that provider starts from. A built-in profile keeps its name and cannot be deleted.</summary>
public sealed class EditModel(MpDbContext db, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public LineProfileInput Input { get; set; } = new();

    public bool IsBuiltIn { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var profile = await Business.Lines.LoadProfileAsync(db, Me, id, HttpContext.RequestAborted);
            if (profile is null)
            {
                return NotFound();
            }

            Input = Business.Lines.ToInput(profile);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Lines.SaveProfileAsync(db, Me, Id, Input, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Profiles.Saved"]);
                return RedirectToPage(new { id = result.Id });
            }

            AddErrors(result);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id && await Business.Lines.DeleteProfileAsync(db, Me, id, log, HttpContext.RequestAborted) is { } problem)
        {
            FlashError(problem);
            return RedirectToPage(new { id });
        }

        Flash(L["Profiles.Deleted"]);
        return RedirectToPage("/Admin/Profiles/Index");
    }

    private async Task PrepareAsync()
    {
        if (Id is { } id && await Business.Lines.LoadProfileAsync(db, Me, id, HttpContext.RequestAborted) is { } saved)
        {
            IsBuiltIn = saved.IsBuiltIn;
        }

        var title = Id is null ? L["Profiles.New"].Value : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Profiles.Title"], "/Admin/Profiles"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Profiles";
    }
}
