using MatPhone.Localization;
using MatPhone.Telephony;
using Microsoft.AspNetCore.Html;

namespace MatPhone.UiKit;

/// <summary>The state of a person as a pill: a word and a tone (the colour is never alone, COLOR-04).</summary>
public static class Presence
{
    public static string Label(PresenceState state) => Text.T("Labels.Presence." + state);

    public static string Tone(PresenceState state) => state switch
    {
        PresenceState.Idle => "ok",
        PresenceState.Ringing => "warn",
        PresenceState.Busy => "error",
        PresenceState.DoNotDisturb => "busy",
        _ => "plain",
    };

    public static string Key(PresenceState state) => state switch
    {
        PresenceState.Idle => "idle",
        PresenceState.Ringing => "ringing",
        PresenceState.Busy => "busy",
        PresenceState.DoNotDisturb => "dnd",
        _ => "offline",
    };

    public static IHtmlContent Pill(PresenceState state) => Ui.Pill(Label(state), Tone(state));
}
