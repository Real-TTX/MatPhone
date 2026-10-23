using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Services;
using MatPhone.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatPhone.Pages.Admin.Mailboxes;

/// <summary>One mailbox: whose it is, its PIN, the mail of a new message and its greeting.</summary>
public sealed class EditModel(MpDbContext db, ConfigStore config, FileStoreService files, ActivityLogger log) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public MailboxInput Input { get; set; } = new();

    public bool HasPin { get; private set; }

    public bool HasGreeting { get; private set; }

    public bool MailConfigured => config.Current.Mail.IsConfigured;

    public List<SelectListItem> People { get; private set; } = [];

    public List<SelectListItem> Groups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var box = await Business.Mailboxes.LoadAsync(db, Me, id, HttpContext.RequestAborted);
            if (box is null)
            {
                return NotFound();
            }

            Input = Business.Mailboxes.ToInput(box);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await Business.Mailboxes.SaveAsync(db, Me, Id, Input, log, HttpContext.RequestAborted);
            if (result.Ok)
            {
                Flash(L["Mailboxes.Saved"]);
                return RedirectToPage(new { id = result.Id });
            }

            AddErrors(result);
        }

        await PrepareAsync();
        return Page();
    }

    /// <summary>A WAV file as the greeting, or (remove) back to the tone.</summary>
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

        return RedirectToPage(new { id = mailboxId });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is { } id && await Business.Mailboxes.DeleteAsync(db, Me, id, log, HttpContext.RequestAborted) is { } problem)
        {
            FlashError(problem);
            return RedirectToPage(new { id });
        }

        Flash(L["Mailboxes.Deleted"]);
        return RedirectToPage("/Admin/Mailboxes/Index");
    }

    private async Task PrepareAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (Id is { } id && await Business.Mailboxes.LoadAsync(db, Me, id, ct) is { } saved)
        {
            HasPin = saved.PinHash is not null;
            HasGreeting = saved.GreetingFileId is not null;
        }

        People = Controls.Choices.Of(await Lookups.UsersAsync(db, Me, ct));
        Groups = Controls.Choices.Of(await Lookups.GroupsAsync(db, Me, ct));
        var title = Id is null ? L["Mailboxes.New"].Value : Input.Name;
        Head(title, null, (L["Admin.Settings"], null), (L["Mailboxes.Title"], "/Admin/Mailboxes"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Mailboxes";
    }
}
