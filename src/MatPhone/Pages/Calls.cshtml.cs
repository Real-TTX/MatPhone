using System.Globalization;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Html;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Pages;

/// <summary>The calls of the person: who called, whom the person called, what was missed; a call back starts the web client with the number.</summary>
public sealed class CallsModel(MpDbContext db, ConfigStore config, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Head(L["Calls.Title"], L["Calls.Sub"], (L["Calls.Title"], null));
        var now = clock.GetUtcNow().UtcDateTime;
        var today = Fmt.UtcFromLocal(Fmt.Local(now).Date);
        var spec = new ListSpec<CallView>
        {
            Id = "my-calls",
            Action = "/Calls",
            Label = L["Calls.Title"],
            Noun = "Ui.Noun.Call",
            SearchPlaceholder = L["Calls.Search"],
            Search = CallLog.SearchMine,
            Filters =
            [
                new FilterSpec<CallView>("kind", L["Calls.Kind"], [new("missed", L["Calls.Kind.Missed"]), new("in", L["Calls.Kind.In"]), new("out", L["Calls.Kind.Out"])], CallLog.FilterMine),
                new FilterSpec<CallView>("period", L["Calls.Period"], [new("today", L["Calls.Period.Today"]), new("7", L["Calls.Period.Week"]), new("30", L["Calls.Period.Month"])],
                    (q, v) =>
                    {
                        var since = v == "today" ? today : now.AddDays(-int.Parse(v, CultureInfo.InvariantCulture));
                        return q.Where(c => c.StartDate >= since);
                    }),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<CallView>, bool, IOrderedQueryable<CallView>>)>
            {
                ["start"] = (L["Calls.When"], (q, d) => d ? q.OrderByDescending(c => c.StartDate) : q.OrderBy(c => c.StartDate)),
                ["duration"] = (L["Calls.Duration"], (q, d) => d ? q.OrderByDescending(c => c.DurationSeconds) : q.OrderBy(c => c.DurationSeconds)),
            },
            DefaultSort = "start",
            DefaultDescending = true,
            PageSize = 30,
        };

        var result = await ListBuilder.RunAsync(CallLog.Mine(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var plan = PhoneNumbers.Plan(config.Current.Telephony);
        Table = TableModel.Of("my-calls", L["Calls.Title"], result.Items,
        [
            (new TableColumn(L["Calls.Who"], Primary: true), c => Who(c, plan)),
            (new TableColumn(L["Calls.When"], KeepLabel: true), c => Ui.Text(Fmt.Moment(c.StartDate))),
            (new TableColumn(L["Calls.Result"]), c => Labels.Pill(Missed(c) ? CallResult.Missed : c.Result)),
            (new TableColumn(L["Calls.Duration"], Numeric: true, HideOnPhone: true), c => c.DurationSeconds > 0 ? Ui.Text(Fmt.Duration(c.DurationSeconds)) : Ui.None()),
        ],
        c => Other(c) is { Length: > 0 } number ? [new RowAction("phone", L["Calls.CallBack"], "/Phone?dial=" + Uri.EscapeDataString(number))] : []) with
        {
            EmptyTitle = L["Calls.Empty.Title"],
            EmptyText = L["Calls.Empty.Text"],
            EmptyIcon = "phone",
        };
    }

    /// <summary>A call that rang for the person and that the person did not take (another member of the group may have).</summary>
    private static bool Missed(CallView c) => !c.IsCaller && !c.Answered && c.Result is CallResult.Answered;

    private static string? Other(CallView c) => c.IsCaller ? c.CalledNumber : c.CallerNumber;

    private IHtmlContent Who(CallView c, NumberPlan plan)
    {
        var number = NumberRules.Display(Other(c), plan);
        var name = c.IsCaller ? null : c.CallerName;
        var direction = c.IsCaller ? "phone-outgoing" : c.Result is CallResult.Missed or CallResult.Voicemail || Missed(c) ? "phone-missed" : "phone-incoming";
        var label = c.IsCaller ? L["Calls.Kind.Out"].Value : L["Calls.Kind.In"].Value;
        return Ui.Concat(new HtmlString($"<span class=\"mp-call-dir\" role=\"img\" aria-label=\"{System.Net.WebUtility.HtmlEncode(label)}\">"), Ui.Icon(direction), new HtmlString("</span>"),
            Ui.NameWithSub(name ?? (number.Length > 0 ? number : L["Phone.Unknown"].Value), null, name is null ? null : number));
    }
}
