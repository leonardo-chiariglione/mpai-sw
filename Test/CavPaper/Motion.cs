using Mpai.Cav.Driving;
using P = Mpai.Cav.Ams.Planning;

namespace Mpai.Cav.Paper;

// THE ONE INTERFACE (paper Secs. III, VI): the scene description in (the Scene of step 1, as the Environment Sensing Subsystem hands it over, and the
// motion inference of step 2 where the implementation wants it), the motion out (a Decision: the acceleration and the way, Polyline, the CAV follows).
// Every implementation below is behind it; the simulation, the sensing, the scene description and the vehicle are the same for all.
public interface IMotion
{
    string Name { get; }
    Decision Decide(Scene scene, IReadOnlyList<Prediction> predictions);
}

// What the vehicle can do on this surface (the VRV of AIMs\CAV\V2.0\Vrv: friction 0.9 dry asphalt, 0.6 wet asphalt, 0.15 ice): the most it can brake
// and the most it can turn, m/s2 (friction x g, and never more than the 8 m/s2 of the vehicle's brakes).
public sealed record Surface(string Name, double Friction)
{
    public double Grip => Math.Min(8.0, Friction * 9.81);
    public static readonly Surface Dry = new("dry", 0.9), Wet = new("wet", 0.6), Ice = new("ice", 0.15);
}

// (a) THE DRIVING CORE of N-19/N-20 (AIMs\CAV\V2.0\Driving): its own motion inference respected, "no collision, otherwise stop". It is told the grip of the
// surface (as the AMS has it from the VRV): its braking figures are held within it.
public sealed class CoreMotion(RoadMap map, string startLane, string destLane, double destS, Surface surface) : IMotion
{
    private readonly Driver driver = new(map, startLane, destLane, destS)
    {
        EmergencyBraking = Math.Min(8.0, surface.Grip), FallbackBraking = Math.Min(5.0, 0.7 * surface.Grip), ComfortBraking = Math.Min(3.0, 0.45 * surface.Grip),
        LateralAcceleration = Math.Min(2.0, 0.4 * surface.Grip), MaxAcceleration = Math.Min(2.0, 0.4 * surface.Grip)
    };
    public string Name => "core";
    public Decision Decide(Scene scene, IReadOnlyList<Prediction> predictions) => driver.Decide(scene, predictions);
}

// (b) A PLAIN CAR-FOLLOWING BASELINE: the Intelligent Driver Model behind the nearest road user in its own way (the lanes of its route, no lane change
// of its own), no motion inference: it brakes only for what is in its lane now. Its speed: the limit of the lane it is in, held for curves within the grip.
public sealed class IdmMotion : IMotion
{
    private readonly RoadMap map; private readonly Polyline route; private readonly Surface surface;
    private readonly double s0 = 2, T = 1.5, a = 2, b = 3;
    public string Name => "idm";

    public IdmMotion(RoadMap map, IReadOnlyList<string> routeLanes, Surface surface)
    {
        this.map = map; this.surface = surface;
        route = RoadMap.Line(routeLanes.Select(id => map.Lanes[id]).ToList());
    }

    public Decision Decide(Scene scene, IReadOnlyList<Prediction> predictions)
    {
        var ego = scene.Ego;
        var (sEgo, _) = route.Project(ego.Position);
        var limit = ego.LaneId is { } l && map.Lanes.TryGetValue(l, out var lane) ? lane.SpeedLimit : map.Lanes.Values.First().SpeedLimit;
        // the curve ahead: no faster than the grip allows (v^2 kappa within 0.4 of it)
        for (var ds = 0.0; ds <= 60; ds += 5)
        {
            var k = Math.Abs(route.CurvatureAt(Math.Min(route.Length, sEgo + ds)));
            if (k > 1e-4) limit = Math.Min(limit, Math.Sqrt(0.4 * surface.Grip / k));
        }
        // the leader: the nearest road user ahead whose outline is within the CAV's lane on its way
        (double Gap, double V, int Id)? leader = null;
        foreach (var u in scene.RoadUsers)
        {
            var (s, d) = route.Project(u.Position);
            if (s <= sEgo || s > sEgo + 150) continue;
            if (Math.Abs(d) > 1.75 + u.Width / 2 - 0.2) continue;
            var gap = s - sEgo - (u.Length + 4.6) / 2;
            var v = u.Velocity.Dot(Vec2.FromHeading(route.HeadingAt(s)));
            if (leader is null || gap < leader.Value.Gap) leader = (gap, v, u.Id);
        }
        var vEgo = ego.Speed;
        var free = 1 - Math.Pow(vEgo / Math.Max(1, limit), 4);
        var acc = a * free;
        var reason = $"IDM free road, limit {limit:F1} m/s";
        if (leader is { } ld)
        {
            var dv = vEgo - ld.V;
            var sStar = s0 + Math.Max(0, vEgo * T + vEgo * dv / (2 * Math.Sqrt(a * b)));
            acc = a * (free - Math.Pow(sStar / Math.Max(0.1, ld.Gap), 2));
            reason = $"IDM following #{ld.Id} gap {ld.Gap:F1} m";
        }
        acc = Math.Clamp(acc, -Math.Min(8.0, surface.Grip), 2.0);
        var option = new PathOption("route", route, null, 0, limit);
        return new Decision(scene.Time, "route", acc, acc, acc < -6, reason, 1, limit, option);
    }
}

// (c) THE SPACETIME PLANNER of AIMs\CAV\V2.0\Ams\Planning (M3287 Motion Constraints): it tracks the road users itself (its own ObjectTracker on the
// road users of the Scene, with the lines of the road map) and plans a trajectory each step; its acceleration and its offset from the route are the motion.
public sealed class PlannerMotion : IMotion
{
    private readonly RoadMap map; private readonly Polyline route; private readonly P.PolyPath path;
    private readonly P.SpaceTimePlanner planner; private readonly P.ObjectTracker tracker; private readonly IReadOnlyList<double> offsets;
    public string Name => "planner";

    public PlannerMotion(RoadMap map, IReadOnlyList<string> routeLanes, Surface surface)
    {
        this.map = map;
        route = RoadMap.Line(routeLanes.Select(id => map.Lanes[id]).ToList());
        path = new P.PolyPath(route.Points.Select(p => (p.X, p.Y)));
        planner = new P.SpaceTimePlanner(new P.PlannerSettings { MaxBrake = Math.Min(8.0, surface.Grip), HardBrake = Math.Min(5.0, 0.7 * surface.Grip), ComfortBrake = Math.Min(2.5, 0.4 * surface.Grip) });
        planner.Grip = surface.Friction;
        tracker = new P.ObjectTracker(new Roads(map));
        // the lanes beside the route at its start: the CAV's own (0) and those to its left/right going the same way
        var first = map.Lanes[routeLanes[0]];
        var list = new List<double> { 0 };
        if (first.Left is { } lf) list.Add(first.Width);
        if (first.Right is { } rt) list.Add(-first.Width);
        offsets = list;
    }

    public Decision Decide(Scene scene, IReadOnlyList<Prediction> predictions)
    {
        tracker.Update(scene.Time, scene.RoadUsers.Select(u => new P.Observation(u.Id.ToString(), scene.Time, u.Position.X, u.Position.Y, u.Velocity.X, u.Velocity.Y, u.Length, u.Width,
            u.Kind switch { RoadUserKind.Pedestrian => "person", RoadUserKind.Cyclist => "bicycle", RoadUserKind.Truck => "truck", RoadUserKind.Obstacle => "obstacle", _ => "car" })));
        var ego = scene.Ego;
        var limit = ego.LaneId is { } l && map.Lanes.TryGetValue(l, out var lane) ? lane.SpeedLimit : map.Lanes.Values.First().SpeedLimit;
        var plan = planner.Next(new P.EgoState(scene.Time, ego.Position.X, ego.Position.Y, ego.Heading, ego.Speed), path, limit, tracker.Objects, offsets);
        var way = Math.Abs(plan.Offset) < 0.05 ? route : route.Offset(plan.Offset);
        var option = new PathOption($"offset {plan.Offset:F1}", way, null, 0, limit);
        return new Decision(scene.Time, option.Label, plan.Acceleration, plan.Acceleration, plan.Acceleration < -6, plan.Detail ?? plan.Why ?? "plan", plan.Candidates, limit, option);
    }

    // The road map as the planner's road model: where a vehicle here, going this way, can go on (the lines of the lanes it is in and of those after them).
    private sealed class Roads(RoadMap map) : P.IRoadModel
    {
        public IReadOnlyList<(string Name, P.PolyPath Path)> Continuations(double x, double y, double heading)
        {
            var result = new List<(string, P.PolyPath)>();
            foreach (var (lane, s, _) in map.Matches(new Vec2(x, y), heading).Take(2))
                foreach (var chain in map.Chains(lane, s, 80, 3))
                {
                    var line = RoadMap.Line(chain);
                    var (s0, _) = line.Project(new Vec2(x, y));
                    var pts = line.Points.Where(p => line.Project(p).S >= s0 - 0.5).Select(p => (p.X, p.Y)).Prepend((x, y)).ToList();
                    if (pts.Count >= 2) result.Add((string.Join(">", chain.Select(c => c.Id)), new P.PolyPath(pts)));
                }
            return result;
        }
    }
}
