using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Groups;

/// <summary>One call group: how it rings, who is in it (in which order, logged in or not), what happens when nobody answers, and its numbers.</summary>
public sealed class EditModel(MpDbContext db, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public CallGroupInput Input { get; set; } = new();

    public List<CallGroupMember> Members { get; private set; } = [];

    public List<PhoneNumber> Numbers { get; private set; } = [];

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> OtherGroups { get; private set; } = [];

    public List<SelectListItem> Mailboxes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var group = await CallGroups.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (group is null)
            {
                return NotFound();
            }

            Input = CallGroups.ToInput(group);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await CallGroups.SaveAsync(db, Me, Id, Input, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Groups.Saved"]);
                return RedirectToPage(new { id = result.Id, tab = Tab });
            }

            AddErrors(result);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id && await CallGroups.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted) is { } problem)
        {
            FlashError(problem);
            return RedirectToPage(new { id });
        }

        Flash(L["Groups.Deleted"]);
        return RedirectToPage("/Admin/Groups/Index");
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (Id is { } id && await CallGroups.LoadAsync(db, Me, id, ct) is { } saved)
        {
            Members = [.. saved.Members.OrderBy(m => m.Position)];
            Numbers = await CallGroups.NumbersAsync(db, Me, id, ct);
        }

        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        OtherGroups = Controls.Choices.Of((await Lookups.GroupsAsync(db, Me, ct)).Where(g => g.Id != Id));
        Mailboxes = Controls.Choices.Of(await Lookups.MailboxesAsync(db, Me, ct));
        var title = Id is null ? L["Groups.New"].Value : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Groups.Title"], "/Admin/Groups"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Groups";
    }
}
