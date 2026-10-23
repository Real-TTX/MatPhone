using System.Globalization;
using MatPhone.Business;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.UiKit;

namespace MatPhone.Pages.Admin.Logs;

/// <summary>What the telephony engine reported: registrations, lines, failed calls, refused requests (and every SIP message while the trace is on).</summary>
public sealed class TelephonyModel(MpDbContext db, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public IReadOnlyList<TelephonyLog> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Head(L["Logs.Telephony.Title"], L["Logs.Telephony.Sub"], (L["Admin.Settings"], null), (L["Logs.Telephony.Title"], null));
        var now = clock.GetUtcNow().UtcDateTime;
        var spec = new ListSpec<TelephonyLog>
        {
            Id = "telephony-log",
            Action = "/Admin/Logs/Telephony",
            Label = L["Logs.Telephony.Title"],
            Noun = "Ui.Noun.Entry",
            SearchPlaceholder = L["Logs.Search"],
            Search = TelephonyLogs.Search,
            Filters =
            [
                new FilterSpec<TelephonyLog>("level", L["Logs.Level"], ListBuilder.EnumOptions<LogSeverity>(Labels.Of), (q, v) => q.Where(l => l.Level == Enum.Parse<LogSeverity>(v))),
                new FilterSpec<TelephonyLog>("category", L["Logs.Category"], ListBuilder.EnumOptions<LogCategory>(Labels.Of), (q, v) => q.Where(l => l.Category == Enum.Parse<LogCategory>(v))),
                new FilterSpec<TelephonyLog>("period", L["Calls.Period"], [new("1", L["Logs.Period.Hour"]), new("24", L["Logs.Period.Day"]), new("168", L["Calls.Period.Week"])],
                    (q, v) =>
                    {
                        var since = now.AddHours(-int.Parse(v, CultureInfo.InvariantCulture));
                        return q.Where(l => l.OccurredDate >= since);
                    }),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<TelephonyLog>, bool, IOrderedQueryable<TelephonyLog>>)>
            {
                ["when"] = (L["Logs.When"], (q, d) => d ? q.OrderByDescending(l => l.OccurredDate).ThenByDescending(l => l.Id) : q.OrderBy(l => l.OccurredDate).ThenBy(l => l.Id)),
            },
            DefaultSort = "when",
            DefaultDescending = true,
            PageSize = 100,
        };

        var result = await ListBuilder.RunAsync(TelephonyLogs.Query(db, Me), Request, spec, HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        Entries = result.Items;
    }
}
