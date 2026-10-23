using System.Globalization;
using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Telephony;
using MatPhone.UiKit;

namespace MatPhone.Pages;

/// <summary>The colleagues: name, extension, teams and whether they are free right now; a call starts the web client.</summary>
public sealed class DirectoryModel(MpDbContext db, IPbxStatus pbx) : PageModelBase
{
    private Dictionary<long, string> _extensions = [];
    private Dictionary<long, List<Team>> _teams = [];

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public IReadOnlyList<User> People { get; private set; } = [];

    public long? MeId => Me.UserId;

    public PresenceState State(long id) => pbx.PresenceOf(id);

    public string? Extension(long id) => _extensions.TryGetValue(id, out var ext) ? ext.Split(", ")[0] : null;

    public List<Team> TeamsOf(long id) => _teams.TryGetValue(id, out var teams) ? teams : [];

    public async Task OnGetAsync()
    {
        Head(L["Directory.Title"], L["Directory.Sub"], (L["Directory.Title"], null));
        var ct = HttpContext.RequestAborted;
        var teams = await Business.Teams.ListAsync(db, Me, ct);
        var spec = new ListSpec<User>
        {
            Id = "people",
            Action = "/Directory",
            Label = L["Directory.Title"],
            Noun = "Ui.Noun.Colleague",
            SearchPlaceholder = L["Directory.Search"],
            Search = Business.Users.Search,
            Filters =
            [
                new FilterSpec<User>("team", L["Shell.Nav.Teams"], [.. teams.Select(t => new FilterOption(t.Id.ToString(CultureInfo.InvariantCulture), t.Name))],
                    (q, v) => Business.Users.InTeam(q, db, long.Parse(v, CultureInfo.InvariantCulture))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<User>, bool, IOrderedQueryable<User>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(u => u.DisplayName) : q.OrderBy(u => u.DisplayName)),
            },
            DefaultSort = "name",
            PageSize = 50,
        };

        var result = await ListBuilder.RunAsync(Business.Users.Colleagues(db, Me), Request, spec, ct);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        People = result.Items;
        _extensions = await Business.Users.ExtensionsAsync(db, Me, People.Select(p => p.Id), ct);
        _teams = teams.SelectMany(t => t.Members.Select(m => (m.UserId, Team: t))).GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Team).ToList());
    }
}
