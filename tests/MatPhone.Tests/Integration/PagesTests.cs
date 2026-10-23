using System.Net;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatPhone.Tests.Integration;

/// <summary>Every page of the application opens, with the sample data and as the people who may see it, and the roles keep to their side.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PagesTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PagesTests(AppFixture app) => _app = app;

    private Task<bool> DemoAsync() => _app.WithScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<MpDbContext>();
        var admin = await db.User.FirstAsync(u => u.Role == Data.Entities.UserRole.Admin);
        var config = sp.GetRequiredService<ConfigStore>().Current;
        return await Demo.CreateAsync(db, new Actor(admin.Id, admin.DisplayName, true), sp.GetRequiredService<SecretProtector>(), PhoneNumbers.Plan(config.Telephony));
    });

    [DatabaseFact]
    public async Task Every_page_opens_for_an_administrator_and_the_sample_data_is_written_once()
    {
        await _app.EnsureAdminAsync();
        await DemoAsync();
        Assert.False(await DemoAsync());
        Assert.True(await _app.WithScopeAsync(sp => sp.GetRequiredService<MpDbContext>().PhoneNumber.CountAsync()) >= 6);

        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "admin@example.com", "Start12345");

        var ids = await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MpDbContext>();
            return new Dictionary<string, long>
            {
                ["user"] = await db.User.Select(u => u.Id).MaxAsync(),
                ["team"] = await db.Team.Select(t => t.Id).FirstAsync(),
                ["group"] = await db.CallGroup.Select(g => g.Id).FirstAsync(),
                ["number"] = await db.PhoneNumber.Select(n => n.Id).FirstAsync(),
                ["device"] = await db.Device.Select(d => d.Id).FirstAsync(),
                ["mailbox"] = await db.Mailbox.Select(m => m.Id).FirstAsync(),
                ["line"] = await db.Line.Select(l => l.Id).FirstAsync(),
                ["profile"] = await db.LineProfile.Select(p => p.Id).FirstAsync(),
                ["rule"] = await db.Rule.Select(r => r.Id).FirstAsync(),
                ["keys"] = await db.KeyTemplate.Select(k => k.Id).FirstAsync(),
            };
        });

        string[] urls =
        [
            "/", "/Phone", "/Calls", "/Calls?result=missed", "/Voicemail", "/Directory", "/Account/Telephony", "/Account/Profile", "/Account/Security", "/About",
            "/Admin/Users", "/Admin/Users/Edit", $"/Admin/Users/Edit?id={ids["user"]}", "/Admin/Teams", "/Admin/Teams/Edit", $"/Admin/Teams/Edit?id={ids["team"]}",
            "/Admin/Groups", "/Admin/Groups/Edit", $"/Admin/Groups/Edit?id={ids["group"]}", "/Admin/Numbers", "/Admin/Numbers/Edit", $"/Admin/Numbers/Edit?id={ids["number"]}",
            "/Admin/Devices", "/Admin/Devices/Edit", $"/Admin/Devices/Edit?id={ids["device"]}", "/Admin/Mailboxes", "/Admin/Mailboxes/Edit", $"/Admin/Mailboxes/Edit?id={ids["mailbox"]}",
            "/Admin/Keys", "/Admin/Keys/Edit", $"/Admin/Keys/Edit?id={ids["keys"]}", "/Admin/Lines", "/Admin/Lines/Edit", $"/Admin/Lines/Edit?id={ids["line"]}",
            $"/Admin/Lines/Edit?profile={ids["profile"]}", "/Admin/Profiles", "/Admin/Profiles/Edit", $"/Admin/Profiles/Edit?id={ids["profile"]}",
            "/Admin/Rules", "/Admin/Rules?direction=outbound", "/Admin/Rules/Edit", $"/Admin/Rules/Edit?id={ids["rule"]}",
            "/Admin/Logs/Calls", "/Admin/Logs/Telephony", "/Admin/Logs/Retention", "/Admin/Activity", "/Admin/Status", "/Admin/Telephony", "/Admin/Mail", "/Admin/Storage",
            "/Admin/Backup", "/Admin/Backup/Plans", "/Admin/Backup/Plans/Edit", "/Admin/Backup/Targets", "/Admin/Backup/Targets/Edit", "/Admin/Backup/Restore",
            "/Admin/Security", "/Admin/System",
        ];

        var failures = new List<string>();
        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            var text = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : "";
            if (response.StatusCode != HttpStatusCode.OK || text.Contains("Exception", StringComparison.Ordinal) || !text.Contains("<h1", StringComparison.Ordinal))
            {
                failures.Add($"{(int)response.StatusCode} {url}");
            }
        }

        Assert.True(failures.Count == 0, "Pages that failed: " + string.Join(", ", failures));

        // the export of the call log
        var csv = await client.GetAsync("/Admin/Logs/Calls?handler=Csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
    }

    [DatabaseFact]
    public async Task A_message_is_heard_by_its_owner_only()
    {
        await _app.EnsureAdminAsync();
        var (_, message, _) = await _app.CreateMessageWithAudioAsync("Hoeren");

        // a mailbox of nobody: the administrator may listen, a user may not
        using var admin = _app.NewClient();
        await AppFixture.LoginAsync(admin, "admin@example.com", "Start12345");
        var audio = await admin.GetAsync($"/voicemail/{message.Id}.wav");
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/wav", audio.Content.Headers.ContentType?.MediaType);

        await _app.CreateUserAsync("hoeren@example.test", "Hoeren-Passwort-2026", Data.Entities.UserRole.User);
        using var user = _app.NewClient();
        await AppFixture.LoginAsync(user, "hoeren@example.test", "Hoeren-Passwort-2026");
        Assert.NotEqual(HttpStatusCode.OK, (await user.GetAsync($"/voicemail/{message.Id}.wav")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_user_stays_out_of_the_administration_and_the_anonymous_visitor_is_sent_to_the_sign_in()
    {
        await _app.EnsureAdminAsync();
        await _app.CreateUserAsync("normal@example.test", "Normal-Passwort-2026", Data.Entities.UserRole.User);

        using var anonymous = _app.NewClient();
        foreach (var url in new[] { "/", "/Phone", "/Calls", "/Voicemail", "/Admin/Users", "/Admin/Lines", "/voicemail/1.wav", "/api/voicemail/count" })
        {
            var status = (await anonymous.GetAsync(url)).StatusCode;
            Assert.True(status is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, $"{url} answered {(int)status} to somebody who is not signed in");
        }

        using var user = _app.NewClient();
        await AppFixture.LoginAsync(user, "normal@example.test", "Normal-Passwort-2026");
        foreach (var url in new[] { "/", "/Phone", "/Calls", "/Voicemail", "/Directory", "/Account/Telephony" })
        {
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(url)).StatusCode);
        }

        foreach (var url in new[] { "/Admin/Users", "/Admin/Lines", "/Admin/Rules", "/Admin/Numbers", "/Admin/Backup", "/Admin/System", "/Admin/Mail", "/Admin/Security", "/Admin/Telephony" })
        {
            Assert.NotEqual(HttpStatusCode.OK, (await user.GetAsync(url)).StatusCode);
        }

        // the menu of a user has no administration
        Assert.DoesNotContain("/Admin/Users", await AppFixture.TextAsync(user, "/Calls"));
    }

    [DatabaseFact]
    public async Task Robots_and_llms_are_plain_text_without_a_sign_in()
    {
        using var anonymous = _app.NewClient();
        var robots = await anonymous.GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, robots.StatusCode);
        Assert.Contains("Disallow: /", await robots.Content.ReadAsStringAsync());
        var llms = await anonymous.GetAsync("/llms.txt");
        Assert.Equal(HttpStatusCode.OK, llms.StatusCode);
        Assert.Contains("MatPhone", await llms.Content.ReadAsStringAsync());
    }
}
