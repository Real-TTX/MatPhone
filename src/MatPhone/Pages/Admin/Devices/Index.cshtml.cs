using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Telephony;
using MatPhone.Services;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Devices;

/// <summary>The SIP phones: who they belong to and whether they are registered right now.</summary>
public sealed class IndexModel(MpDbContext db, IPbxStatus pbx) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Devices.Title"], L["Devices.Sub"], (L["Admin.Settings"], null), (L["Devices.Title"], null));
        var spec = new ListSpec<Device>
        {
            Id = "devices",
            Action = "/Admin/Devices",
            Label = L["Devices.Title"],
            Noun = "Ui.Noun.Device",
            SearchPlaceholder = L["Devices.Search"],
            Search = Business.Devices.Search,
            Filters =
            [
                new FilterSpec<Device>("kind", L["Devices.Kind"], ListBuilder.EnumOptions<DeviceKind>(Labels.Of), (q, v) => q.Where(d => d.Kind == Enum.Parse<DeviceKind>(v))),
                new FilterSpec<Device>("status", L["Common.Status"], [new("on", L["Common.Active"]), new("off", L["Common.Inactive"])], (q, v) => q.Where(d => d.IsEnabled == (v == "on"))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<Device>, bool, IOrderedQueryable<Device>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name)),
                ["user"] = (L["Devices.User"], (q, d) => d ? q.OrderByDescending(x => x.User!.DisplayName) : q.OrderBy(x => x.User!.DisplayName)),
                ["seen"] = (L["Devices.LastSeen"], (q, d) => d ? q.OrderByDescending(x => x.LastRegisterDate) : q.OrderBy(x => x.LastRegisterDate)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Devices.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var online = pbx.Registrations.Select(r => r.DeviceId).ToHashSet();
        var registered = L["Devices.Registered"].Value;
        var offline = L["Devices.Offline"].Value;
        var disabled = L["Common.Inactive"].Value;
        Table = TableModel.Of("device-table", L["Devices.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), d => Ui.NameWithSub(d.Name, $"/Admin/Devices/Edit?id={d.Id}", string.Join(" ", new[] { d.Vendor, d.Model }.Where(s => !string.IsNullOrWhiteSpace(s))))),
            (new TableColumn(L["Devices.User"]), d => d.User is null ? Ui.None() : Ui.Person(d.User.DisplayName)),
            (new TableColumn(L["Devices.SipUser"], Mono: true, HideOnPhone: true), d => Ui.Mono(d.SipUser)),
            (new TableColumn(L["Devices.LastSeen"], HideOnPhone: true), d => d.LastRegisterDate is null ? Ui.None() : Ui.Text(Fmt.Moment(d.LastRegisterDate))),
            (new TableColumn(L["Common.Status"]), d => !d.IsEnabled ? Ui.Pill(disabled, "plain") : online.Contains(d.Id) ? Ui.Pill(registered, "ok", "signal") : Ui.Pill(offline, "warn")),
        ],
        d => [new RowAction("edit", L["Common.Edit"], $"/Admin/Devices/Edit?id={d.Id}")]) with
        {
            EmptyTitle = L["Devices.Empty.Title"],
            EmptyText = L["Devices.Empty.Text"],
            EmptyIcon = "smartphone",
        };
    }
}
