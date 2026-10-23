using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Groups;

/// <summary>The call groups: a number rings their members by a strategy.</summary>
public sealed class IndexModel(MpDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Groups.Title"], L["Groups.Sub"], (L["Admin.Settings"], null), (L["Groups.Title"], null));
        var spec = new ListSpec<CallGroup>
        {
            Id = "groups",
            Action = "/Admin/Groups",
            Label = L["Groups.Title"],
            Noun = "Ui.Noun.Group",
            SearchPlaceholder = L["Groups.Search"],
            Search = CallGroups.Search,
            Filters =
            [
                new FilterSpec<CallGroup>("strategy", L["Groups.Strategy"], ListBuilder.EnumOptions<RingStrategy>(Labels.Of), (q, v) => q.Where(g => g.Strategy == Enum.Parse<RingStrategy>(v))),
                new FilterSpec<CallGroup>("status", L["Common.Status"], [new("active", L["Common.Active"]), new("off", L["Common.Inactive"])], (q, v) => q.Where(g => g.IsActive == (v == "active"))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<CallGroup>, bool, IOrderedQueryable<CallGroup>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(g => g.Name) : q.OrderBy(g => g.Name)),
                ["members"] = (L["Groups.Members"], (q, d) => d ? q.OrderByDescending(g => g.Members.Count) : q.OrderBy(g => g.Members.Count)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(CallGroups.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var active = L["Common.Active"].Value;
        var inactive = L["Common.Inactive"].Value;
        Table = TableModel.Of("group-table", L["Groups.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), g => Ui.NameWithSub(g.Name, $"/Admin/Groups/Edit?id={g.Id}", g.Description)),
            (new TableColumn(L["Groups.Strategy"]), g => Ui.Text(Labels.Of(g.Strategy))),
            (new TableColumn(L["Groups.LoggedIn"], Numeric: true, KeepLabel: true), g => Ui.Text($"{g.Members.Count(m => m.IsLoggedIn)} / {g.Members.Count}")),
            (new TableColumn(L["Groups.Fallback"], HideOnPhone: true), g => Ui.Text(Labels.Of(g.Fallback))),
            (new TableColumn(L["Common.Status"], HideOnPhone: true), g => g.IsActive ? Ui.Pill(active, "ok") : Ui.Pill(inactive, "plain")),
        ],
        g => [new RowAction("edit", L["Common.Edit"], $"/Admin/Groups/Edit?id={g.Id}")]) with
        {
            EmptyTitle = L["Groups.Empty.Title"],
            EmptyText = L["Groups.Empty.Text"],
            EmptyIcon = "layers",
        };
    }
}
