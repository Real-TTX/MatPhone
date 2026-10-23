using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages;

/// <summary>The messages of the own mailbox and the mailboxes of the own groups: listen (it counts as heard), call back, download, delete.</summary>
public sealed class VoicemailModel(MpDbContext db, ConfigStore config) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public IReadOnlyList<MessageView> Messages { get; private set; } = [];

    public string Show(string? number) => NumberRules.Display(number, PhoneNumbers.Plan(config.Current.Telephony)) is { Length: > 0 } shown ? shown : L["Phone.Unknown"];

    public async Task OnGetAsync()
    {
        Head(L["Voicemail.Title"], L["Voicemail.Sub"], (L["Voicemail.Title"], null));
        var spec = new ListSpec<MessageView>
        {
            Id = "messages",
            Action = "/Voicemail",
            Label = L["Voicemail.Title"],
            Noun = "Ui.Noun.Message",
            SearchPlaceholder = L["Voicemail.Search"],
            Search = Business.Mailboxes.SearchMessages,
            Filters =
            [
                new FilterSpec<MessageView>("state", L["Common.Status"], [new("new", L["Voicemail.New"]), new("heard", L["Voicemail.Heard"])], (q, v) => q.Where(m => m.IsNew == (v == "new"))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<MessageView>, bool, IOrderedQueryable<MessageView>>)>
            {
                ["received"] = (L["Voicemail.Received"], (q, d) => d ? q.OrderByDescending(m => m.ReceivedDate) : q.OrderBy(m => m.ReceivedDate)),
            },
            DefaultSort = "received",
            DefaultDescending = true,
            PageSize = 25,
        };

        var result = await ListBuilder.RunAsync(Business.Mailboxes.Messages(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Messages = result.Items;
    }

    public async Task<IActionResult> OnPostMarkAsync(long id, bool isNew)
    {
        await Business.Mailboxes.MarkAsync(db, Me, id, isNew, HttpContext.RequestAborted);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        if (await Business.Mailboxes.DeleteMessageAsync(db, Me, id, HttpContext.RequestAborted))
        {
            Flash(L["Voicemail.Deleted"]);
        }

        return RedirectToPage();
    }
}
