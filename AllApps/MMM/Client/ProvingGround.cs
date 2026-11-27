using System.Text.Json.Nodes;

namespace Mpai.Mmm.Client;

// THE CAV PROVING GROUND (2026-11-27): an M-Location of the MMM where a Connected Autonomous Vehicle tests what it can do (World/make_proving_ground.py writes it: its roads, exactly).
// It is not a copy of a place of the Universe: what happens there is changed at once by a User holding the Right - the time of day, the weather and the grip of the road are Item
// Properties of its M-Location (MMM-IPR-V3.0: TimeOfDay, Weather, Friction); its traffic lights are Items (Kind TrafficLight) whose Signal the Proving Ground Service changes in turn.
// It is reached through a Gate on the Metaverse Square: an Item (Kind Gate) that gives access to it. The Proving Ground Service holds the Rights to it, its Gate and its traffic lights;
// it is public: any User may enter and look. Its conditions are changed by the Users the Service gives the Right to: a Participant's, an Officer's, the Manager's - not a Visitor's.
public static class ProvingGround
{
    public const string ID = "ProvingGround", Service = "ProvingGroundService", ServiceName = "Proving Ground Service";
    public static string[] Signals { get; private set; } = [];

    public static JsonObject? Environment(JsonObject world) =>
        (world["environments"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e => (string?)e["id"] == ID);

    public static void Setup(MInstance m, JsonObject world)
    {
        if (Environment(world) is not { } e) return;
        var (ex, ey) = ((double)e["x"]!, (double)e["y"]!);
        var b = e["bounds"]!.AsArray().Select(v => (double)v!).ToArray();
        m.AddService(Service);
        m.AddLocation(ID, holder: Service, isPublic: true, x: ex, y: ey, size: Math.Max(b[2] - b[0], b[3] - b[1]));
        var place = m.Get(ID)!;
        foreach (var (name, value) in e["conditions"]!.AsObject()) place.Properties[name] = value!.DeepClone();      // as it starts: noon, clear, a dry road
        var signals = e["signals"]!.AsArray().OfType<JsonObject>().ToList();
        foreach (var s in signals)
        {
            var j = s["junction"]!.AsArray();
            m.AddItem((string)s["id"]!, "OSD-3DO-V1.5", Service, ID, (double)j[0]! - ex, (double)j[1]! - ey, kind: "TrafficLight", properties: [("Signal", JsonValue.Create("Red"))]);
        }
        Signals = signals.Select(s => (string)s["id"]!).ToArray();
        var sq = world["square"]!;
        foreach (var g in (world["gates"] as JsonArray ?? []).OfType<JsonObject>().Where(g => (string?)g["leads"] == ID))
        {
            var side = g["sides"]!.AsArray().OfType<JsonObject>().First(s => (string?)s["at"] == (string?)g["at"]);
            m.AddItem((string)g["id"]!, "OSD-3DO-V1.5", Service, (string)g["at"]!, (double)side["x"]! - (double)sq["x"]!, (double)side["y"]! - (double)sq["y"]!, kind: "Gate",
                      properties: [("OpenState", JsonValue.Create("Open"))]);
            m.SetLeads((string)g["id"]!, ID);
        }
    }

    // The Right to change the conditions of the Proving Ground: Property Change of its M-Location.
    public static string ConditionsID(string user) => $"PA-Conditions-{user}";
    public static JsonObject ConditionsPA(string user) =>
        Uc2.PA("MMM-PCP-V3.0", "PropertyChange", ConditionsID(user), ("RQ1", "Nil", "ItemID", JsonValue.Create(ID)!));

    public static JsonObject Signal(string item, string value) => new()
    {
        ["Header"] = "MMM-IPR-V3.0", ["ItemID"] = item, ["Properties"] = new JsonArray(new JsonObject { ["Name"] = "Signal", ["Value"] = value })
    };

    // The traffic lights in turn, in a cycle of 50 s: north-south green 20 s, amber 3 s, all red 2 s; then east-west the same.
    public static (string NS, string EW) Phase(double seconds)
    {
        var t = seconds % 50;
        return t < 20 ? ("Green", "Red") : t < 23 ? ("Amber", "Red") : t < 25 ? ("Red", "Red") : t < 45 ? ("Red", "Green") : t < 48 ? ("Red", "Amber") : ("Red", "Red");
    }
}

// The Process of the Proving Ground: it gives its Users the Right to change the conditions, by their Account Level, and runs the traffic lights. Pump it from time to time.
public sealed class ProvingGroundService(HttpClient http, MInstance m)
{
    private readonly MmmClient pg = new(http, ProvingGround.Service, m.TokenOf(ProvingGround.Service));
    private readonly HashSet<string> given = [];
    private (string, string)? shown;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public async Task PumpAsync()
    {
        if (m.Get(ProvingGround.ID) is null) return;
        foreach (var user in m.UsersWithLevel())
        {
            if (m.AccountLevelOf(user) is null or "Visitor" || !given.Add(user)) continue;      // a Visitor looks; a Participant, an Officer, the Manager change the conditions
            var pa = ProvingGround.ConditionsPA(user);
            var r = await pg.PerformAsync("Rights Change", Uc2.C(user, with: [pa, Uc2.Rights($"PG-Conditions-Right-{user}", "May", "Granted", ProvingGround.ConditionsID(user))]));
            if (r.Http != 200) given.Remove(user);
        }
        var phase = ProvingGround.Phase(Clock().ToUnixTimeMilliseconds() / 1000.0);
        if (shown == phase) return;
        shown = phase;
        foreach (var id in ProvingGround.Signals)
            await pg.PerformAsync("Property Change", Uc2.C(id, with: ProvingGround.Signal(id, id.EndsWith("-NS", StringComparison.Ordinal) ? phase.NS : phase.EW)));
    }
}
