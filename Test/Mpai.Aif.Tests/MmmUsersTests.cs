using System.Text.Json.Nodes;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Microsoft.AspNetCore.Builder;
using Mpai.Mmm.Server;

namespace Mpai.Aif.Tests;

// H-USER AND A-USER (the author, 2026-11-27). A human registered by its Thalia has an Account and a User, no Persona. At its first entry it deploys its Users: the H-User, which it
// drives itself, with a Persona of its choice, and an A-User, which it commands with words, with one of the military Personae that show it is an A-User. One of each at most; the
// A-User acts with its own token (it places its own Persona) and is kept from one run of the world to the next.
[Trait("Group", "Fast")]
[Collection("MmmMeeting")]
public class MmmUsersTests
{
    private static JsonObject Profile(string human, string user, JsonObject? extra = null, string? choice = null)
    {
        var pp = (JsonObject?)extra?.DeepClone() ?? new JsonObject();
        pp["Processes"] = new JsonArray(new JsonObject
        {
            ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = user }),
            ["PersonaIDOrPersona"] = new JsonArray(choice is null ? new JsonObject { ["PersonaID"] = $"{user}-Persona" } : new JsonObject { ["PersonaID"] = $"{user}-Persona", ["PersonaChoice"] = choice })
        });
        return new JsonObject { ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = human, ["PersonalProfileID"] = $"Profile-{human}", ["PersonalProfile"] = pp };
    }

    private static async Task<(MInstance M, WebApplication App, HttpClient Http)> WorldAsync(JsonObject? state = null)
    {
        var m = new MInstance(Repository.Schemas) { RequireCredentials = true };
        World.Setup(m, Repository.Root);
        m.Catalogue = new HashSet<string>(["Female_Adult_01", "Male_Adult_01", "Military_Male_01", "Military_Male_02", "Sports_Male_01", "Sports_Female_01"], StringComparer.Ordinal);
        if (state is not null) m.RestoreState(state);
        var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        return (m, app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static Task<MmmClient.Result> Register(HttpClient http, string human, JsonObject profile, string? token = null) =>
        new MmmClient(http, human, token).PerformAsync("Register", new Complements { With = [profile] });

    private static JsonObject Account(string first, string last) => new()
    {
        ["AccountLevel"] = "Participant", ["Credential"] = "secret1",
        ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = first, ["LastName"] = last, ["Organisation"] = "MPAI", ["Email"] = $"{first.ToLowerInvariant()}@example.com" })
    };

    [Fact]
    public async Task AtTheFirstEntryAHumanDeploysAnHUserAndAnAUser()
    {
        var (m, app, http) = await WorldAsync();
        JsonObject state;
        try
        {
            // the Account, as Thalia opens it: no Persona
            var opened = await Register(http, "Leo-human", Profile("Leo-human", "Leo", Account("Leo", "Chiari")));
            Assert.Equal(201, opened.Http);
            var token = opened.Tokens["Leo"];

            // the H-User's Persona may not be one of the A-Users'
            Assert.Equal(422, (await Register(http, "Leo-human", Profile("Leo-human", "Leo", choice: "Military_Male_01"), token)).Http);
            var h = await Register(http, "Leo-human", Profile("Leo-human", "Leo", choice: "Female_Adult_01"), token);
            Assert.Equal(200, h.Http);
            token = h.Tokens["Leo"];
            Assert.False(m.IsAUser("Leo"));

            // the A-User, with a military Persona; a second one is refused
            var a = await Register(http, "Leo-human", Profile("Leo-human", "Leo-A", choice: "Military_Male_01"), token);
            Assert.Equal(200, a.Http);
            Assert.True(m.IsAUser("Leo-A"));
            Assert.Equal("Leo-A", m.AUserOf("Leo-human"));
            Assert.Equal(422, (await Register(http, "Leo-human", Profile("Leo-human", "Leo-B", choice: "Military_Male_02"), a.Tokens["Leo"])).Http);
            Assert.Contains("Leo-A", m.Get("Account-Leo-human")!.Body!["Processes"]!.ToJsonString());

            // the A-User places its own Persona, with its own token; the H-User's client is told of it
            var aUser = new MmmClient(http, "Leo-A", a.Tokens["Leo-A"]);
            Assert.Equal(200, (await aUser.PerformAsync("MM-Add", Uc2.C("Leo-A-Persona", at: "MVS", with: Uc2.SpatialAttitude("SA-Leo-A", 2, 0)))).Http);
            var info = m.InfoOf("Leo")!;
            Assert.Equal("Leo-A", (string?)info["AUser"]?["User"]);
            Assert.Equal("Leo-A-Persona", (string?)info["AUser"]?["Persona"]);
            Assert.True((bool?)m.InfoOf("Leo-A")!["Autonomous"]);

            // another human's H-User may not take an A-User's Persona as its own
            var bea = await Register(http, "Bea-human", Profile("Bea-human", "Bea", Account("Bea", "Neri")));
            Assert.Equal(422, (await Register(http, "Bea-human", Profile("Bea-human", "Bea", choice: "Military_Male_02"), bea.Tokens["Bea"])).Http);

            state = m.SaveState();
        }
        finally { await app.StopAsync(); }

        // the world restarts: the A-User is still Leo's A-User (the Visitor test below runs on a world of its own)
        var (m2, app2, _) = await WorldAsync(state);
        try
        {
            Assert.True(m2.IsAUser("Leo-A"));
            Assert.False(m2.IsAUser("Leo"));
            Assert.Equal("Leo-A", m2.AUserOf("Leo-human"));
        }
        finally { await app2.StopAsync(); }
    }

    // THE ROLE AT THE FIRST ENTRY, OFFICER BY REQUEST, NEVER (2026-11-27). Thalia opens an Account with all four of first name, last name, organisation and email, and no Account
    // Level; the person chooses it at its first entry, once. An Officer request goes to the MMM Manager, who answers it in the Manager Console; the person is told once. Never to
    // an A-User is kept, also over a restart.
    [Fact]
    public async Task TheRoleIsChosenAtTheFirstEntryAndAnOfficerRequestIsAnsweredByTheManager()
    {
        var (m, app, http) = await WorldAsync();
        JsonObject state;
        try
        {
            m.ManagerEmail = "boss@example.com";
            var noLevel = new JsonObject { ["Credential"] = "secret1", ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = "Olga", ["LastName"] = "Uff", ["Organisation"] = "ACME", ["Email"] = "olga@example.com" }) };
            var incomplete = new JsonObject { ["Credential"] = "secret1", ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = "Olga", ["LastName"] = "Uff" }) };
            Assert.Equal(422, (await Register(http, "Olga-human", Profile("Olga-human", "Olga", incomplete))).Http);          // all four are needed
            var olga = await Register(http, "Olga-human", Profile("Olga-human", "Olga", noLevel));
            Assert.Equal(201, olga.Http);
            Assert.Null((string?)m.InfoOf("Olga")!["AccountLevel"]);

            // the role, chosen once: Officer asked, a Participant meanwhile (the server registers it so and makes the request)
            var role = new JsonObject { ["AccountLevel"] = "Participant" };
            var chosen = await Register(http, "Olga-human", Profile("Olga-human", "Olga", role), olga.Tokens["Olga"]);
            Assert.Equal(200, chosen.Http);
            Assert.Equal("Participant", (string?)m.InfoOf("Olga")!["AccountLevel"]);
            var again = await Register(http, "Olga-human", Profile("Olga-human", "Olga", new JsonObject { ["AccountLevel"] = "Visitor" }), chosen.Tokens["Olga"]);
            Assert.Equal(200, again.Http);
            Assert.Equal("Participant", (string?)m.InfoOf("Olga")!["AccountLevel"]);      // chosen once
            m.RequestOfficer("Olga-human");
            Assert.Equal("Pending", (string?)m.InfoOf("Olga")!["OfficerRequest"]?["Status"]);

            // the Manager Console: only the Manager sees the requests and answers them
            var boss = await Register(http, "Boss-human", Profile("Boss-human", "Boss", Account("Boss", "Capo")));
            Assert.True(m.IsManager("Boss"));
            HttpRequestMessage As(HttpMethod method, string url, string token) { var r = new HttpRequestMessage(method, url); r.Headers.Authorization = new("Bearer", token); return r; }
            var token = again.Tokens["Olga"];                                                 // each entry gives new tokens
            Assert.Equal(403, (int)(await http.SendAsync(As(HttpMethod.Get, "/viewer/manager/requests", token))).StatusCode);
            var list = await http.SendAsync(As(HttpMethod.Get, "/viewer/manager/requests", boss.Tokens["Boss"]));
            Assert.Contains("olga@example.com", await list.Content.ReadAsStringAsync());
            Assert.Equal(200, (int)(await http.SendAsync(As(HttpMethod.Post, "/viewer/manager/answer?human=Olga-human&approve=true", boss.Tokens["Boss"]))).StatusCode);
            Assert.Equal("Officer", m.LevelNameOf("Olga"));
            Assert.Equal("Approved", (string?)m.InfoOf("Olga")!["OfficerRequest"]?["Status"]);
            Assert.Equal(200, (int)(await http.SendAsync(As(HttpMethod.Post, "/viewer/officer/seen", token))).StatusCode);
            Assert.Null(m.InfoOf("Olga")!["OfficerRequest"]);                               // told once

            // Never to an A-User
            Assert.Equal(200, (int)(await http.SendAsync(As(HttpMethod.Post, "/viewer/auser/never", token))).StatusCode);
            Assert.True((bool?)m.InfoOf("Olga")!["AUserNever"]);
            state = m.SaveState();
        }
        finally { await app.StopAsync(); }

        var (m2, app2, _) = await WorldAsync(state);
        try
        {
            Assert.True((bool?)m2.InfoOf("Olga")!["AUserNever"]);
            Assert.Equal("Officer", m2.LevelNameOf("Olga"));
        }
        finally { await app2.StopAsync(); }
    }

    // A VISITOR has one H-User, with one of the Visitors' (sports) Personae, and no A-User; a Participant's H-User does not take a Visitor's Persona.
    [Fact]
    public async Task AVisitorHasOneHUserWithAVisitorsPersonaAndNoAUser()
    {
        var (m, app, http) = await WorldAsync();
        try
        {
            var visitorAccount = new JsonObject { ["AccountLevel"] = "Visitor", ["Credential"] = "secret1", ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = "Vito", ["LastName"] = "Ospite" }) };
            Assert.Equal(422, (await Register(http, "Vito-human", Profile("Vito-human", "Vito", visitorAccount, "Female_Adult_01"))).Http);       // not a Visitor's Persona
            var v = await Register(http, "Vito-human", Profile("Vito-human", "Vito", visitorAccount, "Sports_Male_01"));
            Assert.Equal(201, v.Http);
            Assert.Equal(422, (await Register(http, "Vito-human", Profile("Vito-human", "Vito-A", choice: "Military_Male_01"), v.Tokens["Vito"])).Http);   // no A-User
            Assert.Null(m.AUserOf("Vito-human"));

            var pia = await Register(http, "Pia-human", Profile("Pia-human", "Pia", Account("Pia", "Rossi")));
            Assert.Equal(422, (await Register(http, "Pia-human", Profile("Pia-human", "Pia", choice: "Sports_Female_01"), pia.Tokens["Pia"])).Http);
            Assert.Equal(200, (await Register(http, "Pia-human", Profile("Pia-human", "Pia", choice: "Male_Adult_01"), pia.Tokens["Pia"])).Http);
        }
        finally { await app.StopAsync(); }
    }
}
