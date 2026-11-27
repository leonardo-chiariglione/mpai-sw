using System.Text.Json.Nodes;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Microsoft.AspNetCore.Builder;
using Mpai.Mmm.Server;

namespace Mpai.Aif.Tests;

// ACCOUNTS WITH CREDENTIALS (2026-11-26): in the world nobody enters without registering - opening an Account with its level, its data and a password - and afterwards enters with
// the password (Register again). A Visitor gives first name, last name and password; a Participant or an Officer an email and an organisation too. The password is kept as a salted
// hash, never as given, and the Account outlives a restart. At the end of a visit a Visitor's Persona is offered again to whoever registers.
[Trait("Group", "Fast")]
[Collection("MmmMeeting")]
public class MmmAccountsTests
{
    private static JsonObject Profile(string user, JsonObject extra, string? choice = null)
    {
        var pp = (JsonObject)extra.DeepClone();
        pp["Processes"] = new JsonArray(new JsonObject
        {
            ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = user }),
            ["PersonaIDOrPersona"] = new JsonArray(choice is null ? new JsonObject { ["PersonaID"] = $"{user}-Persona" } : new JsonObject { ["PersonaID"] = $"{user}-Persona", ["PersonaChoice"] = choice })
        });
        return new JsonObject { ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = $"{user}-human", ["PersonalProfileID"] = $"Profile-{user}-human", ["PersonalProfile"] = pp };
    }
    private static JsonObject Data(string first, string last, string? org = null, string? mail = null)
    {
        var d = new JsonObject { ["FirstName"] = first, ["LastName"] = last };
        if (org is not null) d["Organisation"] = org;
        if (mail is not null) d["Email"] = mail;
        return d;
    }

    private static async Task<(MInstance M, WebApplication App, HttpClient Http)> WorldAsync(JsonObject? state = null)
    {
        var m = new MInstance(Repository.Schemas) { RequireCredentials = true };
        World.Setup(m, Repository.Root);
        m.Catalogue = new HashSet<string>(["Female_Adult_01", "Male_Adult_01", "Sports_Male_01"], StringComparer.Ordinal);   // Sports_: a Visitor's Persona (2026-11-27)
        if (state is not null) m.RestoreState(state);
        var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        return (m, app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static Task<MmmClient.Result> Register(HttpClient http, string user, JsonObject extra, string? choice = null, string? token = null) =>
        new MmmClient(http, $"{user}-human", token).PerformAsync("Register", new Complements { With = [Profile(user, extra, choice)] });

    // INVITED BY EMAIL: the chair invites the email of an Account; the AVS Service gives that User the meeting's Rights (Rights Change) when it asks to join. The Account carries the email;
    // an Officer whose organisation is MPAI is called an MPAI Officer.
    [Fact]
    public async Task AnInvitationIsTheRightsGivenToTheAccountWithTheInvitedEmail()
    {
        var (m, app, http) = await WorldAsync();
        try
        {
            var services = new MeetingServices(http, m);
            services.PreinviteEmails(["anna@example.com"]);                     // Anna convenes
            await services.StartAsync();
            async Task<MmmClient> Arrive(string user, string first, string last, string level, string org, string mail, string persona)
            {
                var r = await Register(http, user, new JsonObject { ["AccountLevel"] = level, ["Credential"] = "secret1", ["Data"] = new JsonArray(Data(first, last, org, mail)) }, persona);
                Assert.Equal(201, r.Http);
                var u = new MmmClient(http, user, r.Tokens[user]);
                Assert.Equal(200, (await u.PerformAsync("MM-Add", Uc2.C($"{user}-Persona", at: "MVS", with: Uc2.SpatialAttitude($"SA-{user}", 3, 0)))).Http);
                return u;
            }
            async Task<string> Join(MmmClient u)
            {
                var id = $"Join-{u.ProcessID}-{Guid.NewGuid():N}";
                await u.PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Avs, with: Uc2.Message(id, Meeting.JoinText($"{u.ProcessID}-Persona"))));
                await services.PumpAsync();
                return Meeting.TextOf((await u.ReadAsync($"Seat-{id}")).Item) ?? "";
            }
            var anna = await Arrive("AnnaBianchi", "Anna", "Bianchi", "Participant", "MPAI", "Anna@Example.com", "Female_Adult_01");
            var bruno = await Arrive("BrunoNeri", "Bruno", "Neri", "Participant", "ACME", "bruno@example.com", "Male_Adult_01");
            Assert.Equal("Anna@Example.com", m.EmailOf("AnnaBianchi"));
            Assert.Equal("Anna@Example.com", (string?)m.Get("Account-AnnaBianchi-human")!.Body!["Email"]);
            Assert.StartsWith("seat=", await Join(anna));                            // invited: her email (any case)
            Assert.Contains("not invited", await Join(bruno));                       // an Account, but not the invited email
            Assert.Equal(200, (await anna.PerformAsync("MM-Send", Uc2.C("Inv-B", to: Meeting.Avs, with: Uc2.Message("Inv-B", "invite;user=BrunoNeri")))).Http);
            await services.PumpAsync();
            Assert.StartsWith("seat=", await Join(bruno));                           // she convenes: she may invite him
            Assert.Equal("Participant", m.LevelNameOf("AnnaBianchi"));
        }
        finally { http.Dispose(); await app.DisposeAsync(); Meeting.Reset(); }
    }

    // THE MANAGER: not offered; the Account with the Manager's email is the Manager's whatever level it ticked (also an Account opened before), called MMM Manager, with a seat in any meeting.
    [Fact]
    public async Task TheManagerIsTheAccountWithTheManagersEmailAndCoversEveryLevel()
    {
        var (m, app, http) = await WorldAsync();
        try
        {
            Assert.DoesNotContain(m.AccountLevels, l => l.Id == MInstance.Manager);
            var options = await http.GetStringAsync("/api/v2.2/register/options");
            Assert.DoesNotContain($"\"{MInstance.Manager}\"", options);      // not a level offered (the Officer's description names the MMM Manager, 2026-11-27)
            // an Account opened before the Manager was named: it becomes the Manager's when it enters
            Assert.Equal(201, (await Register(http, "Boss", new JsonObject { ["AccountLevel"] = "Participant", ["Credential"] = "secret1", ["Data"] = new JsonArray(Data("Boss", "X", "MPAI", "boss@example.com")) }, "Male_Adult_01")).Http);
            Assert.Equal("Participant", m.LevelNameOf("Boss"));
            m.ManagerEmail = "Boss@Example.com";
            var entered = await Register(http, "Boss", new JsonObject { ["Credential"] = "secret1" });
            Assert.Equal(200, entered.Http);
            Assert.Equal("MMM Manager", m.LevelNameOf("Boss"));
            Assert.True(m.IsOfficerOrAbove("Boss"));
            // a seat in a meeting it was not invited to
            var services = new MeetingServices(http, m);
            services.PreinviteEmails(["someone@example.com"]);
            await services.StartAsync();
            var boss = new MmmClient(http, "Boss", entered.Tokens["Boss"]);
            Assert.Equal(200, (await boss.PerformAsync("MM-Add", Uc2.C("Boss-Persona", at: "MVS", with: Uc2.SpatialAttitude("SA-Boss", 3, 0)))).Http);
            var id = $"Join-Boss-{Guid.NewGuid():N}";
            await boss.PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Avs, with: Uc2.Message(id, Meeting.JoinText("Boss-Persona"))));
            await services.PumpAsync();
            Assert.StartsWith("seat=", Meeting.TextOf((await boss.ReadAsync($"Seat-{id}")).Item));
            // a new Account with the Manager's email is the Manager's whatever it ticked
            m.ManagerEmail = "new.boss@example.com";
            Assert.Equal(201, (await Register(http, "NewBoss", new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "secret2", ["Data"] = new JsonArray(Data("New", "Boss", "MPAI", "new.boss@example.com")) }, "Female_Adult_01")).Http);
            Assert.Equal("MMM Manager", m.LevelNameOf("NewBoss"));
            Assert.Equal(MInstance.Manager, (string?)m.Get("Account-NewBoss-human")!.Body!["AccountLevel"]);
        }
        finally { http.Dispose(); await app.DisposeAsync(); Meeting.Reset(); }
    }

    // A DEVICE LINKED TO AN ACCOUNT (2026-11-26): it enters with its key, not the password; a wrong key is refused; the link outlives a restart; removed, the device enters no more.
    [Fact]
    public async Task ALinkedDeviceEntersWithItsKeyUntilItIsRemoved()
    {
        JsonObject saved; string key, id;
        var (m, app, http) = await WorldAsync();
        try
        {
            Assert.Equal(201, (await Register(http, "Anna", new JsonObject { ["AccountLevel"] = "Participant", ["Credential"] = "secret2", ["Data"] = new JsonArray(Data("Anna", "Bianchi", "MPAI", "anna@example.com")) }, "Female_Adult_01")).Http);
            var linked = m.LinkDevice("Anna", "Thalia on PC1");
            Assert.NotNull(linked);
            (id, key) = linked!.Value;
            Assert.DoesNotContain(key.Split(':')[1], m.SaveState().ToJsonString());          // the key is kept as a hash
            Assert.Equal(200, (await Register(http, "Anna", new JsonObject { ["DeviceKey"] = key })).Http);
            Assert.Equal(403, (await Register(http, "Anna", new JsonObject { ["DeviceKey"] = id + ":wrong" })).Http);
            Assert.Single(m.DevicesOf("Anna"));
            saved = m.SaveState();
        }
        finally { http.Dispose(); await app.DisposeAsync(); Meeting.Reset(); }

        var (m2, app2, http2) = await WorldAsync(saved);
        try
        {
            Assert.Equal(200, (await Register(http2, "Anna", new JsonObject { ["DeviceKey"] = key })).Http);   // after a restart
            Assert.True(m2.UnlinkDevice("Anna", id));
            Assert.Equal(403, (await Register(http2, "Anna", new JsonObject { ["DeviceKey"] = key })).Http);   // removed
            Assert.Equal(200, (await Register(http2, "Anna", new JsonObject { ["Credential"] = "secret2" })).Http);   // the password still opens it
        }
        finally { http2.Dispose(); await app2.DisposeAsync(); Meeting.Reset(); }
    }

    [Fact]
    public void AnOfficerOfMpaiIsAnMpaiOfficer()
    {
        var m = new MInstance(Repository.Schemas) { RequireCredentials = true };
        World.Setup(m, Repository.Root);
        m.Catalogue = new HashSet<string>(["Female_Adult_01", "Male_Adult_01", "Sports_Male_01"], StringComparer.Ordinal);   // Sports_: a Visitor's Persona (2026-11-27)
        Outcome Reg(string user, string org) => m.Perform(null, new ProcessActionRequest
        {
            SourceProcessID = $"{user}-human", Action = "Register",
            Complements = new Complements { With = [Profile(user, new JsonObject { ["AccountLevel"] = "Officer", ["Credential"] = "secret1", ["Data"] = new JsonArray(Data(user, "X", org, $"{user}@example.com")) })] }
        });
        Assert.Equal(201, Reg("Carla", "MPAI").Http);
        Assert.Equal(201, Reg("Dario", "ACME").Http);
        Assert.Equal("MPAI Officer", m.LevelNameOf("Carla"));
        Assert.Equal("Officer", m.LevelNameOf("Dario"));
        Assert.Equal("MPAI Officer", (string?)m.InfoOf("Carla")!["AccountLevelName"]);
        Meeting.Reset();
    }

    [Fact]
    public async Task AnAccountIsOpenedWithItsCredentialsAndEnteredWithThem()
    {
        JsonObject saved;
        var (m, app, http) = await WorldAsync();
        try
        {
            // what each level needs
            Assert.Equal(422, (await Register(http, "MarioRossi", new JsonObject { ["AccountLevel"] = "Visitor", ["Data"] = new JsonArray(Data("Mario", "Rossi")) })).Http);           // no password
            Assert.Equal(422, (await Register(http, "MarioRossi", new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "abc", ["Data"] = new JsonArray(Data("Mario", "Rossi")) })).Http);   // too short
            Assert.Equal(422, (await Register(http, "Anna", new JsonObject { ["AccountLevel"] = "Participant", ["Credential"] = "secret1", ["Data"] = new JsonArray(Data("Anna", "Bianchi")) })).Http);   // no email, no organisation
            var visitor = await Register(http, "MarioRossi", new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "secret1", ["Data"] = new JsonArray(Data("Mario", "Rossi")) }, "Sports_Male_01");
            Assert.Equal(201, visitor.Http);
            var anna = await Register(http, "Anna", new JsonObject { ["AccountLevel"] = "Participant", ["Credential"] = "secret2", ["Data"] = new JsonArray(Data("Anna", "Bianchi", "MPAI", "anna@example.com")) }, "Female_Adult_01");
            Assert.Equal(201, anna.Http);
            // the password is not kept as given: not in the Personal Profile, not in the state
            Assert.Null(m.Get("Profile-Anna-human")!.Body!["PersonalProfile"]!["Credential"]);
            saved = m.SaveState();
            Assert.DoesNotContain("secret2", saved.ToJsonString());
            // entering: the wrong password is refused, the right one gives a new token; an Account that is not there is said so
            Assert.Equal(403, (await Register(http, "Anna", new JsonObject { ["Credential"] = "wrong!" })).Http);
            var entered = await Register(http, "Anna", new JsonObject { ["Credential"] = "secret2" });
            Assert.Equal(200, entered.Http);
            Assert.NotEqual(anna.Tokens["Anna"], entered.Tokens["Anna"]);
            Assert.Equal(404, (await Register(http, "Nobody", new JsonObject { ["Credential"] = "secret9" })).Http);

            // THE END OF A VISIT: Mario's Persona is offered again; Anna (a Participant) keeps hers
            Assert.True(m.PersonaTaken("Sports_Male_01"));
            Assert.True(m.EndVisit("MarioRossi"));
            Assert.False(m.EndVisit("Anna"));
            Assert.False(m.PersonaTaken("Sports_Male_01"));
            Assert.Null(m.Get("MarioRossi-Persona"));
            Assert.NotNull(m.Get("Anna-Persona"));
            Assert.Null(m.TokenOf("MarioRossi"));
            // A VISITOR IS FORGOTTEN (2026-11-27): Mario's Account is gone with his visit - his password opens nothing - and he registers again
            Assert.Null(m.Get("Account-MarioRossi-human"));
            Assert.Equal(404, (await Register(http, "MarioRossi", new JsonObject { ["Credential"] = "secret1" })).Http);
            var back = await Register(http, "MarioRossi", new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "secret1", ["Data"] = new JsonArray(Data("Mario", "Rossi")) }, "Sports_Male_01");
            Assert.Equal(201, back.Http);
            Assert.Equal("MarioRossi-Persona", (string?)m.InfoOf("MarioRossi")!["Persona"]);
            saved = m.SaveState();
        }
        finally { http.Dispose(); await app.DisposeAsync(); Meeting.Reset(); }

        // A VISITOR HEARD FROM NO MORE: its visit ends by itself (the server notes each request a User makes with its token), its Persona offered again
        var before = MmmServer.VisitEnds;
        MmmServer.VisitEnds = TimeSpan.FromSeconds(1);
        var (mv, appv, httpv) = await WorldAsync();
        try
        {
            var v = await Register(httpv, "GinoVerdi", new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "secret3", ["Data"] = new JsonArray(Data("Gino", "Verdi")) }, "Sports_Male_01");
            Assert.Equal(201, v.Http);
            var gino = new MmmClient(httpv, "GinoVerdi", v.Tokens["GinoVerdi"]);
            Assert.Equal(200, (await gino.PerformAsync("MM-Add", Uc2.C("GinoVerdi-Persona", at: "MVS", with: Uc2.SpatialAttitude("SA-Gino", 3, 0)))).Http);
            for (var i = 0; i < 30 && mv.PersonaTaken("Sports_Male_01"); i++) await Task.Delay(1000);
            Assert.False(mv.PersonaTaken("Sports_Male_01"));
            Assert.Null(mv.Get("GinoVerdi-Persona"));
        }
        finally { MmmServer.VisitEnds = before; httpv.Dispose(); await appv.DisposeAsync(); Meeting.Reset(); }

        // AFTER A RESTART the Account is there, and its password still opens it
        var (m2, app2, http2) = await WorldAsync(saved);
        try
        {
            Assert.Equal(403, (await Register(http2, "Anna", new JsonObject { ["Credential"] = "secret1" })).Http);
            Assert.Equal(200, (await Register(http2, "Anna", new JsonObject { ["Credential"] = "secret2" })).Http);
        }
        finally { http2.Dispose(); await app2.DisposeAsync(); Meeting.Reset(); }
    }
}
