using System.Net;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;


namespace MatPhone.UiKit;

/// <summary>An entry of the user's context menu. A <see cref="Post"/> item changes something (sign out): a button of a small form, not a link.</summary>
public sealed record UserMenuItem(string Title, string Href, string Icon, bool Danger = false, bool SeparatorBefore = false, bool Post = false);

/// <summary>Everything the layout needs to draw the shell around a page: the menus, the user, the bell, the version.</summary>
public sealed class ShellModel
{
    public IReadOnlyList<NavSet> Menus { get; init; } = [];

    public bool HasSwitcher => Menus.Count > 1;

    public NavSet CurrentMenu(string? active) => Menus.FirstOrDefault(m => m.Contains(active)) ?? Menus[0];

    public string UserName { get; init; } = "";

    public string UserRole { get; init; } = "";

    public string UserLine { get; init; } = "";

    public IReadOnlyList<UserMenuItem> UserMenu { get; init; } = [];

    public string Version { get; init; } = "";

    public string Tagline { get; init; } = Text.T("Shell.Tagline");

    /// <summary>The unread notifications of the person (the badge of the bell); null draws no bell (a page without a person).</summary>
    public int? Unread { get; init; }

    /// <summary>"light" or "dark" when the user chose one; null follows the system.</summary>
    public string? Theme { get; init; }

    public string HomeHref { get; init; } = "/";

    /// <summary>The optional attributes of &lt;html&gt;, written only when they have a value (an empty data-theme would count as a choice).</summary>
    public string RootAttributes => Theme is "light" or "dark" ? $"data-theme=\"{WebUtility.HtmlEncode(Theme)}\"" : string.Empty;
}


/// <summary>The badges of the menu: new messages and missed calls of the person, lines that are down and a problem of the engine for administrators.</summary>
public sealed record NavCounts(int NewMessages, int MissedToday, int LinesDown, bool EngineProblem);

/// <summary>Builds the shell for the person who is signed in: the administration menu only for administrators.</summary>
public sealed class ShellFactory(ICurrentUser user, MpDbContext db, Telephony.IPbxStatus pbx, IHttpContextAccessor accessor, TimeProvider clock)
{
    public async Task<ShellModel> BuildAsync()
    {
        var principal = accessor.HttpContext?.User;
        var theme = principal?.FindFirst(AppClaims.Theme)?.Value;
        var counts = await CountsAsync();

        var menus = new List<NavSet> { WorkMenu(counts) };
        if (user.IsAdmin)
        {
            menus.Add(AdminMenu(counts));
        }

        return new ShellModel
        {
            Menus = menus,
            UserName = user.Name ?? Text.T("Shell.Guest"),
            UserRole = Labels.Of(user.IsAdmin ? UserRole.Admin : UserRole.User),
            UserLine = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
            UserMenu = UserMenu(),
            Version = $"MatPhone {AppVersion.Current}",
            Unread = user.UserId is null ? null : counts.NewMessages,
            Theme = theme,
        };
    }

    /// <summary>The shell of a page without a menu (sign-in, setup).</summary>
    public static ShellModel Bare(string? theme = null) => new()
    {
        Menus = [new NavSet("none", "MatPhone", "home", [])],
        Version = $"MatPhone {AppVersion.Current}",
        Theme = theme,
    };

    private async Task<NavCounts> CountsAsync()
    {
        if (user.UserId is null)
        {
            return new NavCounts(0, 0, 0, false);
        }

        var actor = Business.Actor.From(user);
        var today = Fmt.UtcFromLocal(Fmt.Local(clock.GetUtcNow().UtcDateTime).Date);
        var messages = await Business.Mailboxes.NewCountAsync(db, actor);
        var missed = await Business.CallLog.MissedSinceAsync(db, actor, today);
        var linesDown = user.IsAdmin ? pbx.Lines.Count(l => l.Status != "–" && !l.Registered) : 0;
        var problem = user.IsAdmin && pbx.Problem is not null;
        return new NavCounts(messages, missed, linesDown, problem);
    }

    private static NavSet WorkMenu(NavCounts c) => new("work", Text.T("Shell.Menu.Work"), "phone",
    [
        new(null,
        [
            new(Text.T("Shell.Nav.Phone"), "/Phone", "phone", Primary: true),
            new(Text.T("Shell.Nav.Overview"), "/", "dashboard"),
        ]),
        new(Text.T("Shell.Group.Mine"),
        [
            new(Text.T("Shell.Nav.Calls"), "/Calls", "phone-incoming", c.MissedToday > 0 ? NavBadge.Count(c.MissedToday, "warn", Text.T("Shell.Badge.Missed")) : null),
            new(Text.T("Shell.Nav.Voicemail"), "/Voicemail", "voicemail", c.NewMessages > 0 ? NavBadge.Count(c.NewMessages, "accent", Text.T("Shell.Badge.NewMessages")) : null),
            new(Text.T("Shell.Nav.Directory"), "/Directory", "contact"),
            new(Text.T("Shell.Nav.MySettings"), "/Account/Telephony", "sliders"),
        ]),
    ]);

    private static NavSet AdminMenu(NavCounts c) => new("admin", Text.T("Shell.Menu.Admin"), "settings",
    [
        new(Text.T("Shell.Group.Organisation"),
        [
            new(Text.T("Shell.Nav.Users"), "/Admin/Users", "user"),
            new(Text.T("Shell.Nav.Teams"), "/Admin/Teams", "users"),
            new(Text.T("Shell.Nav.Groups"), "/Admin/Groups", "layers"),
        ]),
        new(Text.T("Shell.Group.Telephony"),
        [
            new(Text.T("Shell.Nav.Numbers"), "/Admin/Numbers", "hash"),
            new(Text.T("Shell.Nav.Devices"), "/Admin/Devices", "smartphone"),
            new(Text.T("Shell.Nav.Mailboxes"), "/Admin/Mailboxes", "voicemail"),
            new(Text.T("Shell.Nav.Keys"), "/Admin/Keys", "keyboard"),
        ]),
        new(Text.T("Shell.Group.Lines"),
        [
            new(Text.T("Shell.Nav.Lines"), "/Admin/Lines", "radio-tower", c.LinesDown > 0 ? NavBadge.Count(c.LinesDown, "error", Text.T("Shell.Badge.LinesDown")) : null),
            new(Text.T("Shell.Nav.Profiles"), "/Admin/Profiles", "template"),
            new(Text.T("Shell.Nav.Rules"), "/Admin/Rules", "route"),
        ]),
        new(Text.T("Shell.Group.Logs"),
        [
            new(Text.T("Shell.Nav.CallLog"), "/Admin/Logs/Calls", "list"),
            new(Text.T("Shell.Nav.TelephonyLog"), "/Admin/Logs/Telephony", "scroll-text"),
            new(Text.T("Shell.Nav.Activity"), "/Admin/Activity", "activity"),
            new(Text.T("Shell.Nav.Retention"), "/Admin/Logs/Retention", "archive"),
        ]),
        new(Text.T("Shell.Group.System"),
        [
            new(Text.T("Shell.Nav.Status"), "/Admin/Status", "signal", c.EngineProblem ? NavBadge.Dot("warn", Text.T("Shell.Badge.EngineProblem")) : null),
            new(Text.T("Shell.Nav.Telephony"), "/Admin/Telephony", "settings"),
            new(Text.T("Shell.Nav.Mail"), "/Admin/Mail", "mail"),
            new(Text.T("Shell.Nav.Storage"), "/Admin/Storage", "hard-drive"),
            new(Text.T("Shell.Nav.Backup"), null, "archive", Children:
            [
                new(Text.T("Shell.Nav.BackupOverview"), "/Admin/Backup", null),
                new(Text.T("Shell.Nav.BackupPlans"), "/Admin/Backup/Plans", null),
                new(Text.T("Shell.Nav.BackupTargets"), "/Admin/Backup/Targets", null),
                new(Text.T("Shell.Nav.BackupRestore"), "/Admin/Backup/Restore", null),
            ], Id: "backup"),
            new(Text.T("Shell.Nav.Security"), "/Admin/Security", "shield"),
            new(Text.T("Shell.Nav.System"), "/Admin/System", "server"),
        ]),
    ]);

    private static List<UserMenuItem> UserMenu() =>
    [
        new(Text.T("Shell.User.Account"), "/Account/Profile", "user"),
        new(Text.T("Shell.User.Telephony"), "/Account/Telephony", "phone"),
        new(Text.T("Shell.User.Security"), "/Account/Security", "shield"),
        new(Text.T("Shell.User.Logout"), "/Account/Logout", "logout", Danger: true, SeparatorBefore: true, Post: true),
    ];
}
