using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Keys;

/// <summary>One template of function keys: its keys in their order.</summary>
public sealed class EditModel(MpDbContext db, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public KeyTemplateInput Input { get; set; } = new();

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Groups { get; private set; } = [];

    public int Users { get; private set; }

    public int Devices { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var template = await KeyTemplates.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (template is null)
            {
                return NotFound();
            }

            Input = KeyTemplates.ToInput(template);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await KeyTemplates.SaveAsync(db, Me, Id, Input, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Keys.Saved"]);
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
            await KeyTemplates.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted);
            Flash(L["Keys.Deleted"]);
        }

        return RedirectToPage("/Admin/Keys/Index");
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Groups = Controls.Choices.Of(await Lookups.GroupsAsync(db, Me, ct));
        if (Id is { } id)
        {
            (Users, Devices) = await KeyTemplates.UsageAsync(db, Me, id, ct);
        }

        var title = Id is null ? L["Keys.New"].Value : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Keys.Title"], "/Admin/Keys"), (title, null));
        ViewData["ActiveHref"] = "/Admin/Keys";
    }
}
