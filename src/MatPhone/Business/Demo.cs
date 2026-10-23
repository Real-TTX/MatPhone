using MatPhone.Configuration;
using MatPhone.Core.Security;
using MatPhone.Core.Telephony;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Services;
using Microsoft.EntityFrameworkCore;

namespace MatPhone.Business;

/// <summary>
/// Sample data for trying the installation out: four colleagues with extensions and mailboxes, two teams, two groups, a line (switched off: its account is made up),
/// numbers, rules and a template of function keys. Only into an installation without numbers and groups.
/// </summary>
public static class Demo
{
    public static async Task<bool> IsEmptyAsync(MpDbContext db, Actor actor, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        return !await db.PhoneNumber.AnyAsync(ct) && !await db.CallGroup.AnyAsync(ct) && !await db.Device.AnyAsync(ct);
    }

    public static async Task<bool> CreateAsync(MpDbContext db, Actor actor, SecretProtector secrets, NumberPlan plan, CancellationToken ct = default)
    {
        actor.RequireAdmin();
        if (!await IsEmptyAsync(db, actor, ct))
        {
            return false;
        }

        var me = await db.User.FirstAsync(u => u.Id == actor.Me, ct);
        var people = new List<User> { me };
        foreach (var (name, email) in new[] { ("Anna Becker", "anna.becker"), ("Ben Schulz", "ben.schulz"), ("Clara Wolf", "clara.wolf"), ("David Klein", "david.klein") })
        {
            var address = $"{email}@example.com";
            var user = await db.User.FirstOrDefaultAsync(u => u.Email == address, ct);
            if (user is null)
            {
                user = new User { DisplayName = name, Email = address, Role = UserRole.User, PasswordHash = PasswordHasher.Hash(Devices.GeneratePassword(20)), Language = "de" };
                db.User.Add(user);
            }

            people.Add(user);
        }

        await db.SaveChangesAsync(ct);

        var extension = 200;
        foreach (var person in people)
        {
            db.PhoneNumber.Add(new PhoneNumber { Number = (extension++).ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = NumberKind.Internal, Target = TargetKind.User, UserId = person.Id, Description = person.DisplayName });
            if (!await db.Mailbox.AnyAsync(m => m.UserId == person.Id, ct))
            {
                db.Mailbox.Add(new Mailbox { Name = person.DisplayName, UserId = person.Id });
            }
        }

        var (anna, ben, clara, david) = (people[1], people[2], people[3], people[4]);
        db.Team.Add(new Team { Name = "Vertrieb", Color = "#d6336c", Members = [new() { UserId = anna.Id }, new() { UserId = ben.Id }, new() { UserId = me.Id }] });
        db.Team.Add(new Team { Name = "Support", Color = "#0d9488", Members = [new() { UserId = clara.Id }, new() { UserId = david.Id }] });

        var reception = new CallGroup
        {
            Name = "Zentrale", Description = "Alle Anrufe auf die Hauptnummer", Strategy = RingStrategy.Parallel, MaxWaitSeconds = 30, CallerNamePrefix = "Zentrale: ",
            Members = [new() { UserId = anna.Id, Position = 0 }, new() { UserId = ben.Id, Position = 1 }, new() { UserId = me.Id, Position = 2 }],
        };
        var support = new CallGroup
        {
            Name = "Support", Description = "Kundenservice, nacheinander", Strategy = RingStrategy.Linear, RingSeconds = 15, MaxWaitSeconds = 60, CallerNamePrefix = "Support: ",
            Members = [new() { UserId = clara.Id, Position = 0 }, new() { UserId = david.Id, Position = 1 }],
        };
        db.CallGroup.AddRange(reception, support);
        await db.SaveChangesAsync(ct);

        var supportBox = new Mailbox { Name = "Support", CallGroupId = support.Id };
        db.Mailbox.Add(supportBox);
        await db.SaveChangesAsync(ct);
        support.Fallback = FallbackKind.Mailbox;
        support.FallbackMailboxId = supportBox.Id;
        reception.Fallback = FallbackKind.Group;
        reception.FallbackGroupId = support.Id;
        db.PhoneNumber.Add(new PhoneNumber { Number = "300", Kind = NumberKind.Internal, Target = TargetKind.Group, CallGroupId = reception.Id, Description = "Zentrale" });
        db.PhoneNumber.Add(new PhoneNumber { Number = "310", Kind = NumberKind.Internal, Target = TargetKind.Group, CallGroupId = support.Id, Description = "Support" });

        var profile = await db.LineProfile.FirstOrDefaultAsync(p => p.IsBuiltIn && p.Name.StartsWith("Telekom"), ct);
        var line = new Line
        {
            Name = "Telekom (Beispiel)", ProfileId = profile?.Id, Host = profile?.Host ?? "tel.t-online.de", Domain = profile?.Domain, Port = profile?.Port ?? 5060,
            Transport = profile?.Transport ?? SipTransport.Udp, CallerIdMode = profile?.CallerIdMode ?? CallerIdMode.FromHeader, NumberFormat = profile?.NumberFormat ?? NumberFormat.E164Plus,
            UserName = "+4930123456780", PasswordProtected = secrets.Protect(Devices.GeneratePassword()), DefaultCallerNumber = "+4930123456780", IsEnabled = false,
        };
        db.Line.Add(line);
        await db.SaveChangesAsync(ct);
        var main = new PhoneNumber { Number = "+4930123456780", Kind = NumberKind.External, LineId = line.Id, Target = TargetKind.Group, CallGroupId = reception.Id, Description = "Hauptnummer" };
        db.PhoneNumber.Add(main);
        db.PhoneNumber.Add(new PhoneNumber { Number = "+4930123456710", Kind = NumberKind.External, LineId = line.Id, Target = TargetKind.Group, CallGroupId = support.Id, Description = "Support-Hotline" });
        await db.SaveChangesAsync(ct);

        db.Rule.Add(new Rule { Name = "Wochenende: Support-Mailbox", Direction = RuleDirection.Inbound, Position = 0, Days = TimeWindow.Weekend, PhoneNumberId = main.Id,
            Action = RuleAction.Route, Target = TargetKind.Mailbox, TargetMailboxId = supportBox.Id });
        db.Rule.Add(new Rule { Name = "Nachts: Support-Mailbox", Direction = RuleDirection.Inbound, Position = 1, Days = TimeWindow.Weekdays, TimeFrom = new TimeOnly(18, 0), TimeTo = new TimeOnly(8, 0),
            PhoneNumberId = main.Id, Action = RuleAction.Route, Target = TargetKind.Mailbox, TargetMailboxId = supportBox.Id });
        db.Rule.Add(new Rule { Name = "Mehrwertdienste sperren", Direction = RuleDirection.Outbound, Position = 0, Pattern = "0900.", Action = RuleAction.Block });
        db.Rule.Add(new Rule { Name = "Alles über die Telekom", Direction = RuleDirection.Outbound, Position = 1, Action = RuleAction.UseLine, LineId = line.Id });

        var keys = new KeyTemplate { Name = "Standard", Description = "Kollegen, Zentrale, Nicht stören" };
        var position = 0;
        foreach (var person in people)
        {
            keys.Keys.Add(new FunctionKey { Position = position++, Kind = KeyKind.BusyLamp, TargetUserId = person.Id });
        }

        keys.Keys.Add(new FunctionKey { Position = position++, Kind = KeyKind.GroupToggle, TargetGroupId = reception.Id, Label = "Zentrale" });
        keys.Keys.Add(new FunctionKey { Position = position++, Kind = KeyKind.DoNotDisturb, Label = "Nicht stören" });
        keys.Keys.Add(new FunctionKey { Position = position, Kind = KeyKind.Mailbox, Label = "Mailbox" });
        db.KeyTemplate.Add(keys);
        await db.SaveChangesAsync(ct);
        foreach (var person in people)
        {
            person.KeyTemplateId = keys.Id;
        }

        db.Device.Add(new Device
        {
            Name = "Empfang", Kind = DeviceKind.DeskPhone, Vendor = "Yealink", Model = "T54W", MacAddress = "805ec0a1b2c3", SipUser = "empfang",
            SipPasswordProtected = secrets.Protect(Devices.GeneratePassword()), UserId = anna.Id, IsEnabled = true,
        });
        await db.SaveChangesAsync(ct);
        foreach (var change in Enum.GetValues<TelephonyChange>())
        {
            TelephonySignals.Notify(change);
        }

        return true;
    }
}
