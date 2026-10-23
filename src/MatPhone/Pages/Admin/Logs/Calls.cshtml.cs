using System.Globalization;
using System.Text;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Pages.Admin.Logs;

/// <summary>The call log of the installation: every call with who, when, how long and how it ended; as CSV for a spreadsheet.</summary>
public sealed class CallsModel(MpDbContext db, ConfigStore config, TimeProvider clock) : PageModelBase
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public TableModel Table { get; private set; } = null!;

    public string CsvHref => Request.QueryString.HasValue ? $"/Admin/Logs/Calls?handler=Csv&{Request.QueryString.Value!.TrimStart('?')}" : "/Admin/Logs/Calls?handler=Csv";

    public async Task OnGetAsync()
    {
        Head(L["Logs.Calls.Title"], L["Logs.Calls.Sub"], (L["Admin.Settings"], null), (L["Logs.Calls.Title"], null));
        var result = await ListBuilder.RunAsync(CallLog.All(db, Me), Request, Spec(), HttpContext.RequestAborted);
        Toolbar = result.Toolbar;
        Pager = result.Pager;
        var plan = PhoneNumbers.Plan(config.Current.Telephony);
        var names = (await Lookups.UsersAsync(db, Me, HttpContext.RequestAborted)).ToDictionary(c => c.Id, c => c.Name);
        Table = TableModel.Of("call-log", L["Logs.Calls.Title"], result.Items,
        [
            (new TableColumn(L["Calls.When"], KeepLabel: true), c => Ui.Text(Fmt.Moment(c.StartDate))),
            (new TableColumn(L["Calls.From"]), c => Ui.NameWithSub(c.CallerName ?? NumberRules.Display(c.CallerNumber, plan), null, c.CallerName is null ? null : NumberRules.Display(c.CallerNumber, plan))),
            (new TableColumn(L["Calls.To"], Mono: true), c => Ui.Mono(NumberRules.Display(c.CalledNumber, plan))),
            (new TableColumn(L["Calls.Direction"], HideOnPhone: true), c => Ui.Text(Labels.Of(c.Direction))),
            (new TableColumn(L["Calls.Result"]), c => Labels.Pill(c.Result)),
            (new TableColumn(L["Calls.Duration"], Numeric: true, HideOnPhone: true), c => Ui.Text(Fmt.Duration(c.DurationSeconds))),
            (new TableColumn(L["Calls.AnsweredBy"], HideOnPhone: true), c => c.AnsweredByUserId is { } u && names.TryGetValue(u, out var n) ? Ui.Person(n) : Ui.None()),
        ]) with
        {
            EmptyTitle = L["Logs.Calls.Empty.Title"],
            EmptyText = L["Logs.Calls.Empty.Text"],
            EmptyIcon = "phone",
        };
    }

    /// <summary>The calls of the filter as CSV (semicolons, German Excel opens it as it is), at most 50,000 rows.</summary>
    public async Task<IActionResult> OnGetCsvAsync()
    {
        var result = await ListBuilder.RunAsync(CallLog.All(db, Me), Request, Spec(50_000), HttpContext.RequestAborted);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', L["Calls.When"], L["Calls.Direction"], L["Calls.From"], L["Calls.CallerName"], L["Calls.To"], L["Calls.Result"], L["Calls.Duration"]));
        foreach (var c in result.Items)
        {
            sb.AppendLine(string.Join(';', Fmt.Local(c.StartDate).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Labels.Of(c.Direction), Csv(c.CallerNumber),
                Csv(c.CallerName), Csv(c.CalledNumber), Labels.Of(c.Result), c.DurationSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        var stamp = Fmt.Local(clock.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"Anrufe_{stamp}.csv");
    }

    /// <summary>A cell that a spreadsheet could take for a formula is made harmless; one with the separator is quoted.</summary>
    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' && !text.StartsWith('+'))
        {
            text = "'" + text;
        }

        return text.Contains(';') || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    private ListSpec<CallRecord> Spec(int pageSize = 50)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = Fmt.UtcFromLocal(Fmt.Local(now).Date);
        return new ListSpec<CallRecord>
        {
            Id = "calls",
            Action = "/Admin/Logs/Calls",
            Label = L["Logs.Calls.Title"],
            Noun = "Ui.Noun.Call",
            SearchPlaceholder = L["Calls.Search"],
            Search = CallLog.Search,
            Filters =
            [
                new FilterSpec<CallRecord>("period", L["Calls.Period"], [new("today", L["Calls.Period.Today"]), new("7", L["Calls.Period.Week"]), new("30", L["Calls.Period.Month"])],
                    (q, v) =>
                    {
                        var since = v == "today" ? today : now.AddDays(-int.Parse(v, CultureInfo.InvariantCulture));
                        return q.Where(c => c.StartDate >= since);
                    }),
                new FilterSpec<CallRecord>("direction", L["Calls.Direction"], ListBuilder.EnumOptions<CallDirection>(Labels.Of), (q, v) => q.Where(c => c.Direction == Enum.Parse<CallDirection>(v))),
                new FilterSpec<CallRecord>("result", L["Calls.Result"], ListBuilder.EnumOptions<CallResult>(Labels.Of), (q, v) => q.Where(c => c.Result == Enum.Parse<CallResult>(v))),
            ],
            Sorts = new Dictionary<string, (string, Func<IQueryable<CallRecord>, bool, IOrderedQueryable<CallRecord>>)>
            {
                ["start"] = (L["Calls.When"], (q, d) => d ? q.OrderByDescending(c => c.StartDate) : q.OrderBy(c => c.StartDate)),
                ["duration"] = (L["Calls.Duration"], (q, d) => d ? q.OrderByDescending(c => c.DurationSeconds) : q.OrderBy(c => c.DurationSeconds)),
            },
            DefaultSort = "start",
            DefaultDescending = true,
            PageSize = pageSize,
        };
    }
}
