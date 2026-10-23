using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Pages.Admin.Rules;

/// <summary>The rules for calls in or out, in the order they are checked; the first that matches decides.</summary>
public sealed class IndexModel(MpDbContext db, ConfigStore config) : PageModelBase
{
    [BindProperty(SupportsGet = true, Name = "dir")]
    public RuleDirection Direction { get; set; } = RuleDirection.Inbound;

    public LinkTabsModel Tabs { get; private set; } = null!;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public List<Rule> Rules { get; private set; } = [];

    private Dictionary<long, string> _users = [];
    private Dictionary<long, string> _groups = [];
    private Dictionary<long, string> _mailboxes = [];

    public async Task OnGetAsync()
    {
        Head(L["Rules.Title"], L["Rules.Sub"], (L["Admin.Settings"], null), (L["Rules.Title"], null));
        var ct = HttpContext.RequestAborted;
        var spec = new ListSpec<Rule>
        {
            Id = "rules",
            Action = "/Admin/Rules",
            Label = L["Rules.Title"],
            Noun = "Ui.Noun.Rule",
            SearchPlaceholder = L["Rules.Search"],
            Search = Business.Rules.Search,
            Sorts = new Dictionary<string, (string, Func<IQueryable<Rule>, bool, IOrderedQueryable<Rule>>)>
            {
                ["position"] = (L["Rules.Order"], (q, d) => d ? q.OrderByDescending(r => r.Position).ThenByDescending(r => r.Id) : q.OrderBy(r => r.Position).ThenBy(r => r.Id)),
            },
            DefaultSort = "position",
            PageSize = 200,
            Keep = new Dictionary<string, string?> { ["dir"] = Direction.ToString() },
        };

        var result = await ListBuilder.RunAsync(Business.Rules.Query(db, Me, Direction), Request, spec, ct);
        Toolbar = result.Toolbar;
        Rules = [.. result.Items];
        _users = (await Lookups.UsersAsync(db, Me, ct)).ToDictionary(c => c.Id, c => c.Name);
        _groups = (await Lookups.GroupsAsync(db, Me, ct)).ToDictionary(c => c.Id, c => c.Name);
        _mailboxes = (await Lookups.MailboxesAsync(db, Me, ct)).ToDictionary(c => c.Id, c => c.Name);
        Tabs = new LinkTabsModel(L["Rules.Title"],
        [
            new LinkTab("Inbound", L["Labels.Direction.Inbound"], "/Admin/Rules?dir=Inbound", "phone-incoming"),
            new LinkTab("Outbound", L["Labels.Direction.Outbound"], "/Admin/Rules?dir=Outbound", "phone-outgoing"),
        ], Direction.ToString());
    }

    public async Task<IActionResult> OnPostUpAsync(long id)
    {
        await Business.Rules.MoveAsync(db, Me, id, -1, HttpContext.RequestAborted);
        return RedirectToPage(new { dir = Direction });
    }

    public async Task<IActionResult> OnPostDownAsync(long id)
    {
        await Business.Rules.MoveAsync(db, Me, id, 1, HttpContext.RequestAborted);
        return RedirectToPage(new { dir = Direction });
    }

    /// <summary>The conditions of a rule in a few words: the days, the time, the pattern and the number.</summary>
    public IHtmlContent When(Rule r)
    {
        var parts = new List<string>();
        if (r.Days != 0)
        {
            parts.Add(string.Join(", ", Week.Where(d => TimeWindow.DaysOf(r.Days).Contains(d)).Select(Labels.Short)));
        }

        if (r.TimeFrom is { } from && r.TimeTo is { } to)
        {
            parts.Add($"{from:HH\\:mm}–{to:HH\\:mm}");
        }

        if (!string.IsNullOrWhiteSpace(r.Pattern))
        {
            parts.Add(L[r.Direction == RuleDirection.Inbound ? "Rules.When.Caller" : "Rules.When.Dialled", r.Pattern]);
        }

        if (r.PhoneNumber is { } number)
        {
            parts.Add(L["Rules.When.Number", NumberRules.Display(number.Number, PhoneNumbers.Plan(config.Current.Telephony))]);
        }

        if (r.CallerUserId is { } user && _users.TryGetValue(user, out var name))
        {
            parts.Add(L["Rules.When.User", name]);
        }

        return Ui.Text(parts.Count == 0 ? L["Rules.When.Always"].Value : string.Join(" · ", parts));
    }

    /// <summary>What the rule does, in a few words.</summary>
    public IHtmlContent Then(Rule r) => r.Action switch
    {
        RuleAction.Reject => Ui.Pill(Labels.Of(r.Action), "warn"),
        RuleAction.Block => Ui.Pill(Labels.Of(r.Action), "error", "ban"),
        RuleAction.UseLine => Ui.Text(L["Rules.Then.Line", r.Line?.Name ?? "?"] + (r.StripDigits > 0 || !string.IsNullOrEmpty(r.Prefix) ? " · " + L["Rules.Then.Rewrite"] : "")),
        _ => Ui.Text(r.Target switch
        {
            TargetKind.User => L["Rules.Then.User", _users.GetValueOrDefault(r.TargetUserId ?? 0) ?? "?"],
            TargetKind.Group => L["Rules.Then.Group", _groups.GetValueOrDefault(r.TargetGroupId ?? 0) ?? "?"],
            TargetKind.Mailbox => L["Rules.Then.Mailbox", _mailboxes.GetValueOrDefault(r.TargetMailboxId ?? 0) ?? "?"],
            TargetKind.External => L["Rules.Then.Number", r.TargetNumber ?? ""],
            _ => Labels.Of(r.Target),
        }),
    };

    /// <summary>The days of the week from Monday, as people in Germany count them.</summary>
    public static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
}
