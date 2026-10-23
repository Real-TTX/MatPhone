using MatPhone.Data.Entities;
using MatPhone.Localization;
using Microsoft.AspNetCore.Html;

namespace MatPhone.UiKit;

/// <summary>
/// The words for the enums of the data model in the language of the interface (Resources/Texts/Labels.json), and the tone of a status pill
/// (the colour is never alone: the pill carries the word).
/// </summary>
public static class Labels
{
    public static string Of(UserRole r) => Text.T(r == UserRole.Admin ? "Labels.Role.Admin" : "Labels.Role.User");

    public static string Of(RingStrategy s) => Text.T("Labels.Strategy." + s);

    public static string Of(FallbackKind k) => Text.T("Labels.Fallback." + k);

    public static string Of(NumberKind k) => Text.T("Labels.NumberKind." + k);

    public static string Of(TargetKind k) => Text.T("Labels.Target." + k);

    public static string Of(DeviceKind k) => Text.T("Labels.DeviceKind." + k);

    public static string Of(SipTransport t) => t.ToString().ToUpperInvariant();

    public static string Of(CallerIdMode m) => Text.T("Labels.CallerId." + m);

    public static string Of(NumberFormat f) => Text.T("Labels.NumberFormat." + f);

    public static string Of(RuleDirection d) => Text.T("Labels.Direction." + d);

    public static string Of(RuleAction a) => Text.T("Labels.RuleAction." + a);

    public static string Of(KeyKind k) => Text.T("Labels.KeyKind." + k);

    public static string Of(CallDirection d) => Text.T("Labels.CallDirection." + d);

    public static string Of(CallResult r) => Text.T("Labels.CallResult." + r);

    public static string Of(LogSeverity l) => Text.T("Labels.Severity." + l);

    public static string Of(LogCategory c) => Text.T("Labels.Category." + c);

    /// <summary>How a call ended, as a pill: answered calls are calm, missed ones stand out.</summary>
    public static IHtmlContent Pill(CallResult r) => r switch
    {
        CallResult.Answered => Ui.Pill(Of(r), "ok"),
        CallResult.Voicemail => Ui.Pill(Of(r), "info"),
        CallResult.Missed => Ui.Pill(Of(r), "warn"),
        CallResult.Cancelled => Ui.Pill(Of(r), "plain"),
        _ => Ui.Pill(Of(r), "error"),
    };

    public static IHtmlContent Pill(LogSeverity l) => l switch
    {
        LogSeverity.Error => Ui.Pill(Of(l), "error"),
        LogSeverity.Warning => Ui.Pill(Of(l), "warn"),
        _ => Ui.Pill(Of(l), "info"),
    };

    /// <summary>The icon of the direction of a call in a call list.</summary>
    public static string Icon(CallDirection d, CallResult r) => r is CallResult.Missed or CallResult.Busy or CallResult.Rejected or CallResult.Failed or CallResult.Cancelled
        ? "phone-missed"
        : d switch
        {
            CallDirection.Inbound => "phone-incoming",
            CallDirection.Outbound => "phone-outgoing",
            _ => "phone",
        };

    public static string Of(BackupScope s) => Text.T(s == BackupScope.SystemOnly ? "Labels.BackupScope.SystemOnly" : "Labels.BackupScope.All");

    public static string Of(BackupTargetKind k) => Text.T(k == BackupTargetKind.Smb ? "Labels.BackupTarget.Smb" : "Labels.BackupTarget.Folder");

    public static string Of(ScheduleKind k) => k switch
    {
        ScheduleKind.Hourly => Text.T("Labels.Schedule.Hourly"),
        ScheduleKind.Daily => Text.T("Labels.Schedule.Daily"),
        ScheduleKind.Weekly => Text.T("Labels.Schedule.Weekly"),
        _ => Text.T("Labels.Schedule.Monthly"),
    };

    public static string Of(DayOfWeek d) => Services.Fmt.Culture.DateTimeFormat.GetDayName(d);

    public static string Short(DayOfWeek d) => Services.Fmt.Culture.DateTimeFormat.GetAbbreviatedDayName(d);
}
