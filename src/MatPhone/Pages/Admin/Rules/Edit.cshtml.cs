using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Rules;

/// <summary>One rule: when it applies (days, time, a pattern of the number, the number called or who dials) and what it does.</summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public RuleInput Input { get; set; } = new();

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Groups { get; private set; } = [];

    public List<SelectListItem> Mailboxes { get; private set; } = [];

    public List<SelectListItem> Lines { get; private set; } = [];

    public List<SelectListItem> Numbers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(RuleDirection? dir)
    {
        if (Id is { } id)
        {
            var rule = await Business.Rules.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (rule is null)
            {
                return NotFound();
            }

            Input = Business.Rules.ToInput(rule);
        }
        else
        {
            Input.Direction = dir ?? RuleDirection.Inbound;
            Input.Action = Input.Direction == RuleDirection.Inbound ? RuleAction.Route : RuleAction.UseLine;
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Rules.SaveAsync(db, Me, Id, Input, PhoneNumbers.Plan(config.Current.Telephony), log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Rules.Saved"]);
                return RedirectToPage("/Admin/Rules/Index", new { dir = Input.Direction });
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
            await Business.Rules.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted);
            Flash(L["Rules.Deleted"]);
        }

        return RedirectToPage("/Admin/Rules/Index", new { dir = Input.Direction });
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Groups = Controls.Choices.Of(await Lookups.GroupsAsync(db, Me, ct));
        Mailboxes = Controls.Choices.Of(await Lookups.MailboxesAsync(db, Me, ct));
        Lines = Controls.Choices.Of(await Lookups.LinesAsync(db, Me, ct));
        Numbers = Controls.Choices.Of(await Lookups.ExternalNumbersAsync(db, Me, ct));
        var title = Id is null ? (Input.Direction == RuleDirection.Inbound ? L["Rules.NewInbound"].Value : L["Rules.NewOutbound"].Value) : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Rules.Title"], $"/Admin/Rules?dir={Input.Direction}"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Rules";
    }
}
