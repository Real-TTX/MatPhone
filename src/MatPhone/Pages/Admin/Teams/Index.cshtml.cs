using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Teams;

/// <summary>The teams: who works together and sees each other in the panel of the web client.</summary>
public sealed class IndexModel(MpDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Teams.Title"], L["Teams.Sub"], (L["Admin.Settings"], null), (L["Teams.Title"], null));
        var spec = new ListSpec<Team>
        {
            Id = "teams",
            Action = "/Admin/Teams",
            Label = L["Teams.Title"],
            Noun = "Ui.Noun.Team",
            SearchPlaceholder = L["Teams.Search"],
            Search = Business.Teams.Search,
            Sorts = new Dictionary<string, (string, Func<IQueryable<Team>, bool, IOrderedQueryable<Team>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name)),
                ["members"] = (L["Teams.Members"], (q, d) => d ? q.OrderByDescending(t => t.Members.Count) : q.OrderBy(t => t.Members.Count)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Teams.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("team-table", L["Teams.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), t => Ui.Concat(Ui.Swatch(t.Color), Ui.NameWithSub(t.Name, $"/Admin/Teams/Edit?id={t.Id}", t.Description))),
            (new TableColumn(L["Teams.Members"], Numeric: true), t => Ui.Text(Fmt.Integer(t.Members.Count))),
        ],
        t => [new RowAction("edit", L["Common.Edit"], $"/Admin/Teams/Edit?id={t.Id}")]) with
        {
            EmptyTitle = L["Teams.Empty.Title"],
            EmptyText = L["Teams.Empty.Text"],
            EmptyIcon = "users",
        };
    }
}
