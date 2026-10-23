using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Telephony;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Html;

namespace MatPhone.Pages.Admin.Lines;

/// <summary>The lines to the providers and how they stand right now.</summary>
public sealed class IndexModel(MpDbContext db, IPbxStatus pbx) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Lines.Title"], L["Lines.Sub"], (L["Admin.Settings"], null), (L["Lines.Title"], null));
        var spec = new ListSpec<Line>
        {
            Id = "lines",
            Action = "/Admin/Lines",
            Label = L["Lines.Title"],
            Noun = "Ui.Noun.Line",
            SearchPlaceholder = L["Lines.Search"],
            Search = Business.Lines.Search,
            Sorts = new Dictionary<string, (string, Func<IQueryable<Line>, bool, IOrderedQueryable<Line>>)>
            {
                ["name"] = (L["Common.Name"], (q, d) => d ? q.OrderByDescending(l => l.Name) : q.OrderBy(l => l.Name)),
                ["host"] = (L["Lines.Host"], (q, d) => d ? q.OrderByDescending(l => l.Host) : q.OrderBy(l => l.Host)),
            },
            DefaultSort = "name",
        };

        var result = await ListBuilder.RunAsync(Business.Lines.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var live = pbx.Lines.ToDictionary(l => l.LineId);
        Table = TableModel.Of("line-table", L["Lines.Title"], result.Items,
        [
            (new TableColumn(L["Common.Name"]), l => Ui.NameWithSub(l.Name, $"/Admin/Lines/Edit?id={l.Id}", l.Profile?.Name)),
            (new TableColumn(L["Lines.Host"], Mono: true, HideOnPhone: true), l => Ui.Mono($"{l.Host}:{l.Port} {l.Transport.ToString().ToUpperInvariant()}")),
            (new TableColumn(L["Lines.UserName"], Mono: true, HideOnPhone: true), l => Ui.Mono(l.UserName)),
            (new TableColumn(L["Common.Status"]), l => Status(l, live.GetValueOrDefault(l.Id))),
        ],
        l => [new RowAction("edit", L["Common.Edit"], $"/Admin/Lines/Edit?id={l.Id}")]) with
        {
            EmptyTitle = L["Lines.Empty.Title"],
            EmptyText = L["Lines.Empty.Text"],
            EmptyIcon = "radio-tower",
        };
    }

    private IHtmlContent Status(Line line, LineStatus? live)
    {
        if (!line.IsEnabled)
        {
            return Ui.Pill(L["Common.Inactive"], "plain");
        }

        if (!line.Register)
        {
            return Ui.Pill(L["Lines.NoRegistration"], "info");
        }

        return live?.Registered == true
            ? Ui.Pill(L["Lines.Registered"], "ok", "signal")
            : Ui.Concat(Ui.Pill(L["Lines.NotRegistered"], "error"), new HtmlString($"<small class=\"mat-muted mp-sub\">{System.Net.WebUtility.HtmlEncode(live?.Status ?? line.LastStatus ?? "")}</small>"));
    }
}
