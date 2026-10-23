using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.Telephony;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Users;

/// <summary>Everybody who can sign in and phone: administrators and users, with their extension and their state right now.</summary>
public sealed class IndexModel(MpDbContext db, ICurrentUser current, IPbxStatus pbx) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Admin.Users.Title"], L["Admin.Users.Sub"], (L["Admin.Settings"], null), (L["Admin.Users.Title"], null));

        var spec = new ListSpec<User>
        {
            Id = "users",
            Action = "/Admin/Users",
            Label = L["Admin.Users.Title"],
            Noun = L["Admin.Users.Noun"],
            SearchPlaceholder = L["Admin.Users.Search"],
            Search = Business.Users.Search,
            Filters =
            [
                new FilterSpec<User>("role", L["Admin.Users.Role"], ListBuilder.EnumOptions<UserRole>(Labels.Of), (q, v) => q.Where(u => u.Role == Enum.Parse<UserRole>(v))),
                new FilterSpec<User>("status", L["Common.Status"], [new("active", L["Admin.Users.Active"]), new("locked", L["Admin.Users.Locked"])], (q, v) => v == "locked" ? q.Where(u => !u.IsActive) : q.Where(u => u.IsActive)),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<User>, bool, IOrderedQueryable<User>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(u => u.DisplayName) : q.OrderBy(u => u.DisplayName)),
                ["email"] = (L["Common.Email"], (q, d) => d ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email)),
                ["role"] = (L["Admin.Users.Role"], (q, d) => d ? q.OrderByDescending(u => u.Role).ThenBy(u => u.DisplayName) : q.OrderBy(u => u.Role).ThenBy(u => u.DisplayName)),
                ["login"] = (L["Admin.Users.LastLogin"], (q, d) => d ? q.OrderByDescending(u => u.LastLoginDate) : q.OrderBy(u => u.LastLoginDate)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Users.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var extensions = await Business.Users.ExtensionsAsync(db, Me, result.Items.Select(u => u.Id), HttpContext.RequestAborted);
        var me = current.UserId;
        var thatsYou = L["Admin.Users.ThatsYou"].Value;
        var active = L["Admin.Users.Pill.Active"].Value;
        var locked = L["Admin.Users.Pill.Locked"].Value;
        Table = TableModel.Of("user-table", L["Admin.Users.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), u => Ui.NameWithSub(u.DisplayName, $"/Admin/Users/Edit?id={u.Id}", u.Id == me ? thatsYou : u.Email)),
            (new TableColumn(L["Admin.Users.Extension"], Mono: true, KeepLabel: true), u => extensions.TryGetValue(u.Id, out var ext) ? Ui.Mono(ext) : Ui.None()),
            (new TableColumn(L["Admin.Users.Role"], HideOnPhone: true), u => Ui.Text(Labels.Of(u.Role))),
            (new TableColumn(L["Admin.Users.Presence"], HideOnPhone: true), u => Presence.Pill(pbx.PresenceOf(u.Id))),
            (new TableColumn(L["Admin.Users.LastLogin"], HideOnPhone: true), u => Ui.Text(u.LastLoginDate is null ? L["Admin.Users.Never"] : Fmt.Moment(u.LastLoginDate))),
            (new TableColumn(L["Common.Status"]), u => u.IsActive ? Ui.Pill(active, "ok") : Ui.Pill(locked, "error")),
        ],
        u => [new RowAction("edit", L["Common.Edit"], $"/Admin/Users/Edit?id={u.Id}")]) with
        {
            EmptyTitle = L["Admin.Empty.Title"],
            EmptyText = L["Admin.Empty.Text"],
            EmptyIcon = "users",
        };
    }
}
