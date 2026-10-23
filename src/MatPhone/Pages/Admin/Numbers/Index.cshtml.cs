using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Html;

namespace MatPhone.Pages.Admin.Numbers;

/// <summary>The numbers of the installation, internal and external, and what each one rings.</summary>
public sealed class IndexModel(MpDbContext db, ConfigStore config) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Numbers.Title"], L["Numbers.Sub"], (L["Admin.Settings"], null), (L["Numbers.Title"], null));
        var spec = new ListSpec<PhoneNumber>
        {
            Id = "numbers",
            Action = "/Admin/Numbers",
            Label = L["Numbers.Title"],
            Noun = "Ui.Noun.Number",
            SearchPlaceholder = L["Numbers.Search"],
            Search = PhoneNumbers.Search,
            Filters =
            [
                new FilterSpec<PhoneNumber>("kind", L["Numbers.Kind"], ListBuilder.EnumOptions<NumberKind>(Labels.Of), (q, v) => q.Where(n => n.Kind == Enum.Parse<NumberKind>(v))),
                new FilterSpec<PhoneNumber>("target", L["Numbers.Target"], ListBuilder.EnumOptions<TargetKind>(Labels.Of), (q, v) => q.Where(n => n.Target == Enum.Parse<TargetKind>(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<PhoneNumber>, bool, IOrderedQueryable<PhoneNumber>>)>
            {
                ["number"] = (L["Numbers.Number"], (q, d) => d ? q.OrderByDescending(n => n.Kind).ThenByDescending(n => n.Number) : q.OrderBy(n => n.Kind).ThenBy(n => n.Number)),
                ["description"] = (L["Common.Description"], (q, d) => d ? q.OrderByDescending(n => n.Description) : q.OrderBy(n => n.Description)),
            },
            DefaultSort = "number",
            PageSize = 50,
        };

        var result = await ListBuilder.RunAsync(PhoneNumbers.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var plan = PhoneNumbers.Plan(config.Current.Telephony);
        Table = TableModel.Of("number-table", L["Numbers.Title"], result.Items,
        [
            (new TableColumn(L["Numbers.Number"], Mono: true), n => Ui.NameWithSub(NumberRules.Display(n.Number, plan), $"/Admin/Numbers/Edit?id={n.Id}", n.Description)),
            (new TableColumn(L["Numbers.Kind"], HideOnPhone: true), n => Ui.Text(Labels.Of(n.Kind))),
            (new TableColumn(L["Numbers.Target"], KeepLabel: true), Target),
            (new TableColumn(L["Numbers.Line"], HideOnPhone: true), n => n.Line is null ? Ui.None() : Ui.Text(n.Line.Name)),
            (new TableColumn(L["Common.Status"], HideOnPhone: true), n => n.IsActive ? Ui.Pill(L["Common.Active"], "ok") : Ui.Pill(L["Common.Inactive"], "plain")),
        ],
        n => [new RowAction("edit", L["Common.Edit"], $"/Admin/Numbers/Edit?id={n.Id}")]) with
        {
            EmptyTitle = L["Numbers.Empty.Title"],
            EmptyText = L["Numbers.Empty.Text"],
            EmptyIcon = "hash",
        };
    }

    private IHtmlContent Target(PhoneNumber n) => n.Target switch
    {
        TargetKind.User when n.User is { } user => Ui.Concat(Ui.Icon("user"), Ui.Link(user.DisplayName, $"/Admin/Users/Edit?id={user.Id}")),
        TargetKind.Group when n.CallGroup is { } group => Ui.Concat(Ui.Icon("layers"), Ui.Link(group.Name, $"/Admin/Groups/Edit?id={group.Id}")),
        TargetKind.Mailbox when n.Mailbox is { } box => Ui.Concat(Ui.Icon("voicemail"), Ui.Link(box.Name, $"/Admin/Mailboxes/Edit?id={box.Id}")),
        TargetKind.External => Ui.Concat(Ui.Icon("phone-forwarded"), Ui.Mono(n.TargetNumber)),
        _ => Ui.Pill(L["Labels.Target.None"], "warn"),
    };
}
