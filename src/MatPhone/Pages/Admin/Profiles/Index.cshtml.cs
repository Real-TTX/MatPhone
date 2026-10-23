using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Profiles;

/// <summary>The provider profiles: the settings a new line starts from (Telekom, peoplefone, Vodafone ...), the built-in ones and own ones.</summary>
public sealed class IndexModel(MpDbContext db) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Profiles.Title"], L["Profiles.Sub"], (L["Admin.Settings"], null), (L["Profiles.Title"], null));
        var spec = new ListSpec<LineProfile>
        {
            Id = "profiles",
            Action = "/Admin/Profiles",
            Label = L["Profiles.Title"],
            Noun = "Ui.Noun.Profile",
            SearchPlaceholder = L["Profiles.Search"],
            Search = Business.Lines.SearchProfiles,
            Filters =
            [
                new FilterSpec<LineProfile>("origin", L["Profiles.Origin"], [new("builtin", L["Profiles.BuiltIn"]), new("own", L["Profiles.Own"])], (q, v) => q.Where(p => p.IsBuiltIn == (v == "builtin"))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<LineProfile>, bool, IOrderedQueryable<LineProfile>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Lines.Profiles(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var builtIn = L["Profiles.BuiltIn"].Value;
        var own = L["Profiles.Own"].Value;
        Table = TableModel.Of("profile-table", L["Profiles.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), p => Ui.NameWithSub(p.Name, $"/Admin/Profiles/Edit?id={p.Id}", p.Notes is { Length: > 90 } n ? n[..90] + "…" : p.Notes)),
            (new TableColumn(L["Lines.Host"], Mono: true, HideOnPhone: true), p => string.IsNullOrWhiteSpace(p.Host) ? Ui.None() : Ui.Mono($"{p.Host}:{p.Port} {p.Transport.ToString().ToUpperInvariant()}")),
            (new TableColumn(L["Profiles.Origin"]), p => p.IsBuiltIn ? Ui.Pill(builtIn, "info") : Ui.Pill(own, "plain")),
        ],
        p => [new RowAction("edit", L["Common.Edit"], $"/Admin/Profiles/Edit?id={p.Id}"), new RowAction("plus", L["Lines.NewFromProfile"], $"/Admin/Lines/Edit?profile={p.Id}")]) with
        {
            EmptyTitle = L["Profiles.Empty.Title"],
            EmptyText = L["Profiles.Empty.Text"],
            EmptyIcon = "template",
        };
    }
}
