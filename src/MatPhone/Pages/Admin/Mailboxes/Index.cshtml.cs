using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Mailboxes;

/// <summary>The mailboxes of users and groups.</summary>
public sealed class IndexModel(MpDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Mailboxes.Title"], L["Mailboxes.Sub"], (L["Admin.Settings"], null), (L["Mailboxes.Title"], null));
        var spec = new ListSpec<Mailbox>
        {
            Id = "mailboxes",
            Action = "/Admin/Mailboxes",
            Label = L["Mailboxes.Title"],
            Noun = "Ui.Noun.Mailbox",
            SearchPlaceholder = L["Mailboxes.Search"],
            Search = Business.Mailboxes.Search,
            Filters =
            [
                new FilterSpec<Mailbox>("owner", L["Mailboxes.Owner"], [new("user", L["Mailboxes.Owner.User"]), new("group", L["Mailboxes.Owner.Group"]), new("none", L["Mailboxes.Owner.None"])],
                    (q, v) => v switch { "user" => q.Where(m => m.UserId != null), "group" => q.Where(m => m.CallGroupId != null), _ => q.Where(m => m.UserId == null && m.CallGroupId == null) }),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Mailbox>, bool, IOrderedQueryable<Mailbox>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(m => m.Name) : q.OrderBy(m => m.Name)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Mailboxes.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var mail = L["Mailboxes.MailNotify.Short"].Value;
        Table = TableModel.Of("mailbox-table", L["Mailboxes.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), m => Ui.NameWithSub(m.Name, $"/Admin/Mailboxes/Edit?id={m.Id}", m.User?.DisplayName ?? m.CallGroup?.Name)),
            (new TableColumn(L["Mailboxes.Owner"], HideOnPhone: true), m => Ui.Text(m.UserId is not null ? L["Mailboxes.Owner.User"] : m.CallGroupId is not null ? L["Mailboxes.Owner.Group"] : L["Mailboxes.Owner.None"])),
            (new TableColumn(L["Mailboxes.Greeting"], HideOnPhone: true), m => m.GreetingFileId is null ? Ui.Text(L["Mailboxes.Greeting.Tone"]) : Ui.Text(L["Mailboxes.Greeting.Own"])),
            (new TableColumn(L["Mailboxes.Mail"], HideOnPhone: true), m => m.MailNotify ? Ui.Pill(mail, "info", "mail") : Ui.None()),
            (new TableColumn(L["Common.Status"]), m => m.IsActive ? Ui.Pill(L["Common.Active"], "ok") : Ui.Pill(L["Common.Inactive"], "plain")),
        ],
        m => [new RowAction("edit", L["Common.Edit"], $"/Admin/Mailboxes/Edit?id={m.Id}")]) with
        {
            EmptyTitle = L["Mailboxes.Empty.Title"],
            EmptyText = L["Mailboxes.Empty.Text"],
            EmptyIcon = "voicemail",
        };
    }
}
