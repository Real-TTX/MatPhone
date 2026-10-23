using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Keys;

/// <summary>The templates of function keys (busy lamps, speed dials, group in/out ...), given to users and devices.</summary>
public sealed class IndexModel(MpDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Keys.Title"], L["Keys.Sub"], (L["Admin.Settings"], null), (L["Keys.Title"], null));
        var spec = new ListSpec<KeyTemplate>
        {
            Id = "keys",
            Action = "/Admin/Keys",
            Label = L["Keys.Title"],
            Noun = "Ui.Noun.Template",
            SearchPlaceholder = L["Keys.Search"],
            Search = KeyTemplates.Search,
            Sorts = new Dictionary<string, (string, Func<IQueryable<KeyTemplate>, bool, IOrderedQueryable<KeyTemplate>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name)),
                ["keys"] = (L["Keys.Count"], (q, d) => d ? q.OrderByDescending(t => t.Keys.Count) : q.OrderBy(t => t.Keys.Count)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(KeyTemplates.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Table = TableModel.Of("key-table", L["Keys.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), t => Ui.NameWithSub(t.Name, $"/Admin/Keys/Edit?id={t.Id}", t.Description)),
            (new TableColumn(L["Keys.Count"], Numeric: true), t => Ui.Text(Fmt.Integer(t.Keys.Count))),
        ],
        t => [new RowAction("edit", L["Common.Edit"], $"/Admin/Keys/Edit?id={t.Id}")]) with
        {
            EmptyTitle = L["Keys.Empty.Title"],
            EmptyText = L["Keys.Empty.Text"],
            EmptyIcon = "keyboard",
        };
    }
}
