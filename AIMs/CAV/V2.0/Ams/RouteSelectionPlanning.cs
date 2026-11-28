using System.Text.Json.Nodes;

using AIF.Controller;

using Mpai.Cav.Ess;
using Mpai.Cav.Map;

namespace Mpai.Cav.Ams;

// ROUTE SELECTION PLANNING, STAGE 1 (CAV-RSP; M3233 3.4). The Destination is the last
// way point of the Route an AMS-HCI Message requests. From the segment the CAV is on
// - the Full Environment Descriptors place it - the Route of least time: that
// segment, then A* on the map from its end. Given to Path Selection Planning as the
// Route Response of an Interaction, and as a Route. A new Route on a Route Request,
// or when the CAV is found on a segment the Route does not have.
//
// IN DIALOGUE WITH HCI (M3243 3.1). A Destination requested is answered with the
// Routes to it - the one of least time and, where the map has one, the best that
// leaves out a segment of it - in an AMS-HCI Message, each segment with the time the
// CAV is expected at its end; none where the Destination cannot be reached. The CAV
// does not move until HCI selects one with the Route Command Execute (without a
// SelectedRouteID: the first). Requested and executed in one message: the first,
// at once. Suspend and Stop halt the CAV at the end of the segment it is on (at the
// next one's, where it is too near to stop), Resume drives on; a new Destination is
// proposed again, the Route being driven kept until another is executed. Each
// command HCI gives is answered with the Route's state, and Arrived is told when the
// CAV stands at the end of its Route.
//
// THE PASSENGER'S REQUEST AS DOMAIN KNOWLEDGE (M3328 3.7, 2026-11-28). What the passenger said reaches the AMS also as the items of the CAV's Domain Knowledge it named
// (Relevance 1): an action of CAV-TEC (Reference HCIMessage.RequestedRoutes, HCIMessage.SelectedRouteID, HCIMessage.RouteCommand:<command>) and what it is about - a
// place (a Way Point of the map) or a Route proposed (its Route ID). No schema of a request: only identifiers the AMS itself gave, turned here into what an AMS-HCI
// Message would ask. What the AMS does is Domain Knowledge too: with the places, the Routes it proposed and the state of the Route driven, given again at each change.
public sealed class RouteSelectionPlanning(string instanceId) : IAimProcessor, IAimRunner
{
    private const double ArrivedWithin = 15, Standing = 0.2, TooNear = 50;
    private RoadMap? map;
    private string? destination;
    private bool propose, executeFirst;
    private string? selected;
    private List<(string Id, IReadOnlyList<RoadSegment> Segments)> candidates = [];
    private IReadOnlyList<RoadSegment>? route;                 // the Route driven
    private string? routeId, status;
    private long count, messages;

    public string InstanceId { get; } = instanceId;

    // THE DOMAIN KNOWLEDGE OF THE CAV (MMC-DKN, 2026-11-27): what the dialogue of the passenger is told of the CAV's domain - the places of its Offline Map, each with the
    // Way Point it is (its Reference), and what the passenger may ask. Given when the Offline Map comes; Domain Knowledge Retrieval in HCI finds in it what fits what was said.
    public const string KnowledgeType = "MMC-DKN-V2.5";
    public static JsonObject Knowledge(RoadMap map)
    {
        var items = new JsonArray();
        foreach (var w in map.WayPoints.Where(w => w.Name is { Length: > 0 }))
            items.Add(new JsonObject
            {
                ["ItemID"] = w.Id, ["Name"] = w.Name, ["Reference"] = w.Id, ["Source"] = "OSD-BOO-V1.5",
                ["Text"] = $"{w.Name} is a place on the map of the car: the car can take the passenger there."
            });
        items.Add(new JsonObject { ["ItemID"] = "Places", ["Text"] = $"The places the car can take the passenger to: {string.Join(", ", map.WayPoints.Where(w => w.Name is { Length: > 0 }).Select(w => w.Name))}." });
        items.Add(new JsonObject { ["ItemID"] = "Routes", ["Text"] = "Asked for a place, the car proposes the Routes to it with the time each takes, and moves only when the passenger agrees." });
        items.Add(new JsonObject { ["ItemID"] = "Commands", ["Text"] = "On the way the passenger may ask the car to stop, to wait (suspend) and to go on (resume), or to go to another place." });
        return new JsonObject
        {
            ["Header"] = KnowledgeType, ["DomainKnowledgeID"] = $"DKN-{map.Id}",
            ["Domain"] = "A connected autonomous vehicle (a car with no driver) that drives its passenger to the places of its map.",
            ["Items"] = items
        };
    }
    // The knowledge of now: that of the map, the Routes proposed, the state of the Route driven.
    private JsonObject Now()
    {
        var k = Knowledge(map!);
        var items = k["Items"]!.AsArray();
        string Named(string id) => map!.WayPoints.FirstOrDefault(w => w.Id == id)?.Name ?? id;
        for (var i = 0; i < candidates.Count; i++)
        {
            var (id, segments) = candidates[i];
            items.Add(new JsonObject
            {
                ["ItemID"] = id, ["Name"] = i == 0 ? "the first route" : "the second route",
                ["Synonyms"] = i == 0 ? new JsonArray("the first one", "the fastest", "the quickest", "the shorter", "the shortest")
                                      : new JsonArray("the second one", "the other one", "the other route", "the alternative", "the longer"),
                ["Reference"] = id, ["Source"] = AmsTypes.Hci,
                ["Text"] = $"Route {id}, proposed to the {Named(destination!)}{(i == 0 ? ", the quickest" : ", another way")}: {segments.Sum(s => map!.Length(s)):0} m, about {map!.TimeOf(segments):0} s."
            });
        }
        if (status is not null && destination is not null)
            items.Add(new JsonObject
            {
                ["ItemID"] = "RouteNow", ["Source"] = AmsTypes.Hci,
                ["Text"] = status switch
                {
                    "Executing" => $"The car is driving the passenger to the {Named(destination)} (Route {routeId}).",
                    "Suspended" => $"The car has halted on the way to the {Named(destination)}; it goes on when the passenger says so.",
                    "Stopped"   => $"The car has stopped; the trip to the {Named(destination)} is abandoned.",
                    "Arrived"   => $"The car has arrived at the {Named(destination)}.",
                    _ => $"Route {routeId}: {status}."
                }
            });
        return k;
    }

    private static async Task Give(IAimPorts ports, JsonObject knowledge)
    {
        try { await ports.WriteAsync(KnowledgeType, 1, knowledge.ToJsonString()); } catch (Exception) { }      // an L3 without the Port: nothing to give
    }

    public Task<Message> ProcessAsync(Message message) => throw new NotSupportedException("CAV-RSP runs continuously.");

    public async Task RunAsync(IAimPorts ports, AimContext context)
    {
        var replan = false;
        string? command = null;
        while (await ports.SelectAsync(-1, (AmsTypes.Map, 1), (AmsTypes.Hci, 1), (KnowledgeType, 1), (AmsTypes.Interaction, 1), (AmsTypes.Fed, 1)) is { } port)
        {
            if (await ports.ReadAsync(port.DataType, 1, 0) is not { } m) continue;
            var json = JsonNode.Parse(m.Json)!;
            switch (port.DataType)
            {
                case AmsTypes.Map:
                    map = RoadMap.FromOfflineMapObject(json);
                    await Give(ports, Now());
                    continue;
                case KnowledgeType:
                    // The items the passenger named, and the identifiers this AMS gave them.
                    var named = (json["Items"] as JsonArray ?? []).OfType<JsonObject>().Where(i => (double?)i["Relevance"] >= 1).Select(i => (string?)i["Reference"]).OfType<string>().ToList();
                    var place = map is null ? null : named.FirstOrDefault(r => map.WayPoints.Any(w => w.Id == r));
                    var chosenRoute = named.FirstOrDefault(r => candidates.Any(x => x.Id == r));
                    if (named.Contains("HCIMessage.RequestedRoutes") && place is not null)
                    {
                        destination = place; propose = true; candidates = [];
                        executeFirst = false;
                    }
                    if (named.Contains("HCIMessage.SelectedRouteID") && chosenRoute is not null) selected = chosenRoute;
                    if (named.FirstOrDefault(r => r.StartsWith("HCIMessage.RouteCommand:", StringComparison.Ordinal)) is { } said)
                    {
                        command = said["HCIMessage.RouteCommand:".Length..];
                        if (command == "Execute")
                        {
                            selected = chosenRoute ?? selected;
                            if (selected is null && propose) { executeFirst = true; command = null; }
                        }
                    }
                    continue;
                case AmsTypes.Hci:
                    var hci = json["HCIMessage"];
                    if (hci?["RequestedRoutes"]?[0]?["Route"]?["RouteSegments"]?.AsArray().LastOrDefault()?["WayPoint2ID"]?.GetValue<string>() is { } to)
                    {
                        destination = to; propose = true; candidates = [];
                        executeFirst = false;
                    }
                    if (hci?["RouteCommand"]?.GetValue<string>() is { } c)
                    {
                        command = c;
                        if (c == "Execute")
                        {
                            selected = hci["SelectedRouteID"]?.GetValue<string>();
                            if (selected is null && propose) { executeFirst = true; command = null; }
                        }
                    }
                    continue;
                case AmsTypes.Interaction:
                    if (json["RouteRequest"] is not null) replan = true;
                    continue;
            }
            // Full Environment Descriptors: where the CAV is, and how fast.
            if (map is null) continue;
            var segmentId = json["EgoPlacement"]?["SegmentID"]?.GetValue<string>();
            if (segmentId is null) continue;
            var here = map.Segments.First(s => s.Id == segmentId);
            var along = (double?)json["EgoPlacement"]?["Along"] ?? 0;
            var speed = json["EgoSpatialAttitude"] is { } ego ? AmsTypes.Ego(ego).Speed : 0;
            var ms = AmsTypes.Ms(json["FullEnvironmentDescriptorsTime"]);

            if (propose && destination is not null)
            {
                propose = false;
                candidates = Candidates(here, destination);
                if (candidates.Count == 0) context.Report($"no Route from {here.To} to {destination}");
                await Tell(ports, ms, new JsonObject { ["RouteList"] = new JsonArray(candidates.Select(c => (JsonNode)RouteObject(c.Id, c.Segments, ms, true)).ToArray()) });
                await Give(ports, Now());
                if (executeFirst && candidates.Count > 0) { executeFirst = false; command = "Execute"; selected = candidates[0].Id; }
            }

            if (command is not null)
            {
                var c = command; command = null;
                switch (c)
                {
                    case "Execute":
                        var chosen = candidates.FirstOrDefault(x => x.Id == (selected ?? candidates.FirstOrDefault().Id));
                        if (chosen.Segments is null) { context.Report($"Execute: no Route {selected} proposed"); break; }
                        (route, routeId) = (From(chosen.Segments, here), chosen.Id);
                        await Drive(ports, ms);
                        await State(ports, ms, "Executing");
                        break;
                    case "Resume" when route is not null && status == "Suspended":
                        route = From(route, here);
                        await Drive(ports, ms);
                        await State(ports, ms, "Executing");
                        break;
                    case "Suspend" or "Stop" when route is not null && status == "Executing":
                        await Halt(ports, here, along, ms);
                        await State(ports, ms, c == "Stop" ? "Stopped" : "Suspended");
                        break;
                }
                continue;
            }
            if (route is null || status != "Executing") continue;

            // Arrived: standing at the end of the Route.
            if (here == route[^1] && here.To == destination && along >= map.Length(here) - ArrivedWithin && speed < Standing)
            {
                await State(ports, ms, "Arrived");
                continue;
            }
            if (!replan && route.Any(s => s.Id == segmentId)) continue;
            var rest = here.To == destination ? [] : map.FastestRoute(here.To, destination!);
            if (rest is null) { context.Report($"no Route from {here.To} to {destination}"); continue; }
            (route, routeId) = ([here, .. rest], $"RTE{++count:D4}");
            replan = false;
            await Drive(ports, ms);
            await State(ports, ms, "Executing");
        }
    }

    // The Routes to the Destination: the one of least time, and the best of those that
    // leave out one of its segments beyond the one the CAV is on.
    private List<(string, IReadOnlyList<RoadSegment>)> Candidates(RoadSegment here, string to)
    {
        if (map!.WayPoints.All(w => w.Id != to)) return [];                  // a place the map does not have
        var fastest = here.To == to ? [] : map.FastestRoute(here.To, to);
        if (fastest is null) return [];
        var list = new List<(string, IReadOnlyList<RoadSegment>)> { ($"RTE{++count:D4}", [here, .. fastest]) };
        IReadOnlyList<RoadSegment>? best = null;
        foreach (var leftOut in fastest)
        {
            var without = new RoadMap(map!.Id, map.OriginLat, map.OriginLon, map.WayPoints, map.Segments.Where(s => s != leftOut).ToList());
            if (without.FastestRoute(here.To, to) is { } other && (best is null || map.TimeOf(other) < map.TimeOf(best))) best = other;
        }
        if (best is not null) list.Add(($"RTE{++count:D4}", [here, .. best]));
        return list;
    }

    // A Route from the segment the CAV is on: what is behind it left out.
    private static IReadOnlyList<RoadSegment> From(IReadOnlyList<RoadSegment> segments, RoadSegment here)
    {
        var i = segments.ToList().IndexOf(here);
        return i >= 0 ? segments.Skip(i).ToList() : segments;
    }

    // Halting: the Route cut at the end of the segment the CAV is on, or of the next
    // one where that end is too near to stop gently.
    private async Task Halt(IAimPorts ports, RoadSegment here, double along, long ms)
    {
        var rest = From(route!, here);
        var keep = along > map!.Length(here) - TooNear && rest.Count > 1 ? 2 : 1;
        route = rest.Take(keep).ToList();
        await Drive(ports, ms);
        route = rest;                                          // what Resume drives on
    }

    private async Task Drive(IAimPorts ports, long ms)
    {
        var r = RouteObject(routeId!, route!, ms, false);
        await ports.WriteAsync(AmsTypes.Interaction, 1, new JsonObject
        {
            ["Header"] = AmsTypes.Interaction, ["InteractionID"] = $"INT-R{++messages:D4}", ["InteractionTime"] = EssJson.SimpleTime($"INT-R{messages:D4}-T", ms),
            ["RouteResponse"] = r.DeepClone()
        }.ToJsonString());
        await ports.WriteAsync(AmsTypes.Route, 1, r.ToJsonString());
    }

    private async Task State(IAimPorts ports, long ms, string state)
    {
        var changed = status != state;
        status = state;
        await Tell(ports, ms, new JsonObject { ["RouteStatus"] = new JsonObject { ["RouteID"] = routeId, ["Status"] = state } });
        if (changed) await Give(ports, Now());
    }

    private async Task Tell(IAimPorts ports, long ms, JsonObject amsMessage)
    {
        var id = $"AHM-A{++messages:D4}";
        await ports.WriteAsync(AmsTypes.Hci, 1, new JsonObject
        {
            ["Header"] = AmsTypes.Hci, ["AMSHCIMessageID"] = id, ["AMSHCIMessageTime"] = EssJson.SimpleTime(id + "-T", ms),
            ["AMSMessage"] = amsMessage
        }.ToJsonString());
    }

    // A Route; proposed, each segment with the time the CAV is expected at its end.
    private JsonObject RouteObject(string id, IReadOnlyList<RoadSegment> segments, long ms, bool expected)
    {
        var at = (double)ms;
        return new()
        {
            ["Header"] = AmsTypes.Route, ["RouteID"] = id, ["RouteTime"] = EssJson.SimpleTime($"{id}-T", ms),
            ["OfflineMapID"] = map!.Id,
            ["RouteSegments"] = new JsonArray(segments.Select(s =>
            {
                var o = new JsonObject { ["WayPoint1ID"] = s.From, ["WayPoint2ID"] = s.To };
                if (expected)
                {
                    at += map.Length(s) / s.SpeedLimit * 1000;
                    o["EstimatedArrDepSpaceTime"] = EssJson.SimpleTime($"{id}-{s.To}-T", (long)Math.Round(at));
                }
                return (JsonNode)o;
            }).ToArray())
        };
    }
}
