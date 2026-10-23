using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Numbers;

/// <summary>One number: internal or external, its line, and what it rings (a user, a group, a mailbox or another number).</summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public PhoneNumberInput Input { get; set; } = new();

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Groups { get; private set; } = [];

    public List<SelectListItem> Mailboxes { get; private set; } = [];

    public List<SelectListItem> Lines { get; private set; } = [];

    public string Prefix => config.Current.Telephony.OutsideLinePrefix;

    /// <summary>A new number for a group, a user or of a kind can be started from their pages (?group=3, ?user=5, ?kind=External).</summary>
    public async Task<IActionResult> OnGetAsync(long? group, long? user, NumberKind? kind)
    {
        if (Id is { } id)
        {
            var number = await PhoneNumbers.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (number is null)
            {
                return NotFound();
            }

            Input = PhoneNumbers.ToInput(number);
        }
        else
        {
            Input.Kind = kind ?? NumberKind.Internal;
            if (group is { } g)
            {
                Input.Target = TargetKind.Group;
                Input.TargetGroupId = g;
            }
            else if (user is { } u)
            {
                Input.Target = TargetKind.User;
                Input.TargetUserId = u;
            }

            if (Input.Kind == NumberKind.Internal)
            {
                Input.Number = await PhoneNumbers.NextFreeExtensionAsync(db, Me, group is null ? 200 : 300, HttpContext.RequestAborted);
            }
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await PhoneNumbers.SaveAsync(db, Me, Id, Input, PhoneNumbers.Plan(config.Current.Telephony), log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Numbers.Saved"]);
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
            await PhoneNumbers.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted);
            Flash(L["Numbers.Deleted"]);
        }

        return RedirectToPage("/Admin/Numbers/Index");
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Groups = Controls.Choices.Of(await Lookups.GroupsAsync(db, Me, ct));
        Mailboxes = Controls.Choices.Of(await Lookups.MailboxesAsync(db, Me, ct));
        Lines = Controls.Choices.Of(await Lookups.LinesAsync(db, Me, ct));
        var title = Id is null ? L["Numbers.New"].Value : Input.Number;
        Head(title, null, (L["Admin.Settings"], null), (L["Numbers.Title"], "/Admin/Numbers"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Numbers";
    }
}
