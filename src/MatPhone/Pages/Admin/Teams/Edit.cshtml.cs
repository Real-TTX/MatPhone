using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Teams;

/// <summary>One team: its name, its colour and who belongs to it.</summary>
public sealed class EditModel(MpDbContext db, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public TeamInput Input { get; set; } = new();

    public List<SelectListItem> People { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var team = await Business.Teams.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (team is null)
            {
                return NotFound();
            }

            Input = Business.Teams.ToInput(team);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Teams.SaveAsync(db, Me, Id, Input, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Teams.Saved"]);
                return RedirectToPage(new { id = result.Id });
            }

            AddErrors(result);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id)
        {
            await Business.Teams.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted);
            Flash(L["Teams.Deleted"]);
        }

        return RedirectToPage("/Admin/Teams/Index");
    }

    private async Task PrepareAsync()
    {
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, HttpContext.RequestAborted));
        var title = Id is null ? L["Teams.New"].Value : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Teams.Title"], "/Admin/Teams"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Teams";
    }
}
