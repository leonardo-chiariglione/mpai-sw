using System.Text.Json.Nodes;

namespace Mpai.Mmm.Client;

// THE TAXIS OF THE METAVERSE SQUARE (2026-11-28, M3329). The taxis are Items - 3D Model Objects drawn from taxi.glb - waiting at the bays of the taxi rank on Rank Street (World/make_city.py).
// The Taxi Service holds the Rights to them: it is the Process of the taxi operator, the human behind the CAV Processes that drive the taxis. Each taxi has the Item Property Occupancy
// (MMM-IPR-V3.0): Free at the rank, Occupied while a CAV Process has it.
//
// A CAV Process hires a taxi by sending the Taxi Service a Message (MM-Send) that says "hire"; the Service answers with a Message that names the taxi it gives - "Taxi-2" - or "busy", and
// gives the CAV Process the Right to MM-Move that taxi (Rights Change). "release Taxi-2" gives it back: the Service takes the Right away, brings the taxi back to its bay, and it is Free.
public static class Taxis
{
    public const string Service = "TaxiService", Rank = "Taxi-Rank";
    public static IReadOnlyList<(string ID, double X, double Y, double Yaw)> Bays { get; private set; } = [];
    public static string Square { get; private set; } = "MVS";

    public static void Setup(MInstance m, JsonObject world)
    {
        if (world["city"]?["taxiRank"] is not JsonObject rank) return;
        var sq = world["square"]!; var (sx, sy) = ((double)sq["x"]!, (double)sq["y"]!);
        Square = (string)rank["at"]!;
        m.AddService(Service);
        var model = (string)rank["taxiModel"]!;
        Bays = rank["bays"]!.AsArray().OfType<JsonObject>().Select((b, i) => ($"Taxi-{i + 1}", (double)b["x"]! - sx, (double)b["y"]! - sy, (double)b["yaw"]!)).ToList();   // on the Square's frame
        foreach (var (id, x, y, yaw) in Bays)
        {
            m.AddItem(id, "OSD-3DO-V1.5", Service, Square, x, y, yaw, kind: "Object", properties: [("Occupancy", JsonValue.Create("Free"))]);
            m.Get(id)!.Model = model;
        }
    }

    // The Right to drive a taxi: MM-Move of that Item, wherever to.
    public static string DriveID(string taxi) => $"PA-Drive-{taxi}";
    public static JsonObject DrivePA(string taxi) => Uc2.PA("MMM-2MP-V2.2", "MMMove", DriveID(taxi), ("RQ1", "Nil", "ItemID", JsonValue.Create(taxi)!));
    public static JsonObject Occupancy(string taxi, string value) => new()
    {
        ["Header"] = "MMM-IPR-V3.0", ["ItemID"] = taxi, ["Properties"] = new JsonArray(new JsonObject { ["Name"] = "Occupancy", ["Value"] = value })
    };
    public static JsonObject Message(string id, string text) => new()
    {
        ["Header"] = "MMM-MSG-V2.2", ["MInstanceID"] = "MI1", ["MessageID"] = id, ["MessageData"] = new JsonArray(new JsonObject { ["MessageData"] = text })
    };
    public static string TextOf(JsonObject? message) =>
        (message?["MessageData"] as JsonArray)?.OfType<JsonObject>().Select(d => (string?)d["MessageData"]).FirstOrDefault(t => t is not null) ?? "";
    public static JsonObject Place(string id, double x, double y, double yaw)
    {
        var sa = Uc2.SpatialAttitude(id, x, y); sa["Orientation"]!["Orientation"] = new JsonArray(yaw, 0.0, 0.0); return sa;
    }
}

public sealed class TaxiService(HttpClient http, MInstance m)
{
    private readonly MmmClient svc = new(http, Taxis.Service, m.TokenOf(Taxis.Service));
    private readonly HashSet<string> seen = [];
    private readonly Dictionary<string, string> hiredBy = [];          // taxi -> the CAV Process that has it
    private long sent;

    public IReadOnlyDictionary<string, string> Hired => hiredBy;

    public async Task PumpAsync()
    {
        foreach (var (id, from) in m.InboxWithSenders(Taxis.Service))
        {
            if (!seen.Add(id)) continue;
            var text = Taxis.TextOf(m.Get(id)?.Body).Trim();
            if (text.Equals("hire", StringComparison.OrdinalIgnoreCase)) await Reply(from, await HireAsync(from) ?? "busy");
            else if (text.StartsWith("release ", StringComparison.OrdinalIgnoreCase)) await Reply(from, await ReleaseAsync(text[8..].Trim(), from) ? "released" : "not yours");
        }
    }

    // A free taxi for the CAV Process: the Right to drive it, and it is Occupied. Null when every taxi is out.
    public async Task<string?> HireAsync(string process)
    {
        if (hiredBy.FirstOrDefault(h => h.Value == process).Key is { } already) return already;
        foreach (var (taxi, _, _, _) in Taxis.Bays)
        {
            if (hiredBy.ContainsKey(taxi) || (string?)m.Get(taxi)?.Properties["Occupancy"] == "Occupied") continue;
            var r = await svc.PerformAsync("Rights Change", Uc2.C(process, with: [Taxis.DrivePA(taxi), Uc2.Rights($"Taxi-Hire-{taxi}-{process}", "May", "Granted", Taxis.DriveID(taxi))]));
            if (r.Http != 200) return null;
            await svc.PerformAsync("Property Change", Uc2.C(taxi, with: [Taxis.Occupancy(taxi, "Occupied")]));
            hiredBy[taxi] = process;
            return taxi;
        }
        return null;
    }

    // The taxi back: the Right taken away, the taxi at its bay, Free.
    public async Task<bool> ReleaseAsync(string taxi, string process)
    {
        if (!hiredBy.TryGetValue(taxi, out var who) || who != process) return false;
        await svc.PerformAsync("Rights Change", Uc2.C(process, with: [Taxis.DrivePA(taxi), Uc2.Rights($"Taxi-Release-{taxi}-{process}", "May Not", "Granted", Taxis.DriveID(taxi))]));
        var bay = Taxis.Bays.First(b => b.ID == taxi);
        var at = m.Get(taxi)!.Location ?? Taxis.Square;
        await svc.PerformAsync("MM-Move", Uc2.C(taxi, from: at, to: Taxis.Square, with: Taxis.Place($"SA-{taxi}-bay", bay.X, bay.Y, bay.Yaw)));
        await svc.PerformAsync("Property Change", Uc2.C(taxi, with: [Taxis.Occupancy(taxi, "Free")]));
        hiredBy.Remove(taxi);
        return true;
    }

    private Task Reply(string to, string text) =>
        svc.PerformAsync("MM-Send", Uc2.C($"Msg-TaxiService-{++sent}-{to}", to: to, with: [Taxis.Message($"Msg-TaxiService-{sent}", text)]));
}
