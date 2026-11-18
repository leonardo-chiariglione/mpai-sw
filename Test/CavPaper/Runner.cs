using Mpai.Cav.Driving;
using Mpai.Cav.DrivingSim;

namespace Mpai.Cav.Paper;

// One run of the experiments: a scenario of Test\CavDriving (its context and variant), a surface, the sensing, a seed, an injection at the scene
// layer and a motion implementation. The loop is that of Simulation.Run (N-19), with: the surface's grip limiting the vehicle; the sensing range and
// noise; the injection between the scene description and the motion (where the ESS hands its scene over); and the measures of the paper.
public sealed record Config(string Motion, Surface Surface, double Range, double Noise, int Seed, string Injection = "none", bool AgentGrip = false);   // AgentGrip: every vehicle, not only the CAV, within the grip of the surface

public sealed record RunResult(
    string Context, string Variant, string Motion, string Surface, double Range, double Noise, int Seed, string Injection,
    string Result, bool Collision, string? CollisionKind, double CollisionSpeed, bool CloseCall, double MinGap, double MinTtc,
    int HardBrakings, int EmergencyDecisions, bool StoppedShort, double Time, double FreeFlowTime, double MaxJerk, double GripExceeded,
    double Harm, int Injected, double Seconds);

public static class Runner
{
    public const double Dt = 0.1;

    // The scenarios, made again for each run (their road users move): the index into Contexts.All().
    public static int Count => Contexts.All().Count();
    public static Scenario Make(int index) => Contexts.All().ElementAt(index);

    public static RunResult Run(int index, Config c)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sc = Make(index);
        if (c.AgentGrip) foreach (var a in sc.Agents) if (a.Kind != RoadUserKind.Pedestrian) a.Grip = c.Surface.Grip;
        var rng = new Random(c.Seed * 7919 + index);
        var inj = new Random(c.Seed * 104729 + index * 31 + c.Injection.GetHashCode());
        var startLane = sc.Map.Lanes[sc.StartLane];
        var ego = new EgoTruth { Position = startLane.Centre.At(sc.StartS), Heading = startLane.Centre.HeadingAt(sc.StartS), Speed = sc.StartSpeed };
        var world = new World { Map = sc.Map, Agents = sc.Agents, Occluders = sc.Occluders, Ego = ego };
        var sensor = new Sensor(c.Range, c.Noise, 0.03);
        var describer = new SceneDescriber(sc.Map);
        var predictor = new MotionPredictor(sc.Map);
        var arrival = new Driver(sc.Map, sc.StartLane, sc.DestLane, sc.DestS);   // only to say when the CAV has arrived, and its route
        IMotion motion = c.Motion switch
        {
            "core" => new CoreMotion(sc.Map, sc.StartLane, sc.DestLane, sc.DestS, c.Surface),
            "idm" => new IdmMotion(sc.Map, arrival.Route, c.Surface),
            "planner" => new PlannerMotion(sc.Map, arrival.Route, c.Surface),
            _ => throw new ArgumentException(c.Motion)
        };

        // The free-flow time: the length of the route from the start to the destination at the speed limits of its lanes.
        var routeLine = RoadMap.Line(arrival.Route.Select(id => sc.Map.Lanes[id]).ToList());
        var (sStart, _) = routeLine.Project(ego.Position);
        var (sDest, _) = routeLine.Project(sc.Map.Lanes[sc.DestLane].Centre.At(sc.DestS));
        var meanLimit = arrival.Route.Average(id => sc.Map.Lanes[id].SpeedLimit);
        var freeFlow = Math.Max(1, (sDest - sStart) / Math.Max(1, meanLimit));

        string result = "stopped (time out)"; bool collision = false; string? collisionKind = null; double collisionSpeed = 0;
        double minGap = double.MaxValue, minTtc = double.MaxValue; int hard = 0, emergencies = 0, injected = 0; bool wasHard = false, wasEmergency = false;
        double maxJerk = 0, gripExceeded = 0; var accs = new Queue<double>();
        // the stretch of a missed road user: who, until when; the phantom's start
        int? stretchId = null; double stretchUntil = -1; bool stretchDone = false;

        for (world.Time = 0; world.Time <= sc.MaxTime + 1e-9; world.Time = Math.Round(world.Time + Dt, 6))
        {
            var frame = sensor.Sense(world, rng);
            var scene = describer.Describe(frame);
            if (arrival.Arrived(scene)) { result = "arrived"; break; }

            // ---- THE INJECTION, between the scene description and the motion ----
            var users = scene.RoadUsers.ToList();
            var before = users.Count;
            switch (c.Injection)
            {
                case var s when s.StartsWith("miss-p"):                        // each road user dropped from this frame with probability p
                    var p = double.Parse(s[6..], System.Globalization.CultureInfo.InvariantCulture) / 100;
                    users = users.Where(_ => inj.NextDouble() >= p).ToList();
                    break;
                case var s when s.StartsWith("miss-stretch"):                  // the nearest road user dropped for a whole stretch, from the first time one is within 30 m
                    var len = double.Parse(s[12..], System.Globalization.CultureInfo.InvariantCulture);
                    if (!stretchDone && stretchId is null && users.Count > 0)
                    {
                        var near = users.MinBy(u => u.Position.DistanceTo(ego.Position))!;
                        if (near.Position.DistanceTo(ego.Position) < 30) { stretchId = near.Id; stretchUntil = world.Time + len; }
                    }
                    if (stretchId is { } id) { if (world.Time < stretchUntil - 1e-9) users = users.Where(u => u.Id != id).ToList(); else { stretchId = null; stretchDone = true; } }
                    break;
                case var s when s.StartsWith("phantom-"):                      // something that is not there, from 5 s, for one frame or for 1 s
                    var oneFrame = s.EndsWith("-frame");
                    if (world.Time >= 5 - 1e-9 && world.Time < (oneFrame ? 5.05 : 6.0) && Phantom(s, scene, sc.Map) is { } ghost) users.Add(ghost);
                    break;
            }
            injected += Math.Abs(users.Count - before);
            if (c.Injection != "none") scene = scene with { RoadUsers = users };

            var preds = predictor.Predict(scene);
            var decision = motion.Decide(scene, preds);
            if (decision.EmergencyStop && ego.Speed > 0.5 && !wasEmergency) emergencies++;
            wasEmergency = decision.EmergencyStop && ego.Speed > 0.5;

            // ---- THE VEHICLE: braking within the grip of the surface; turning beyond it is counted ----
            double acc = Math.Clamp(decision.Acceleration, -Math.Min(8, c.Surface.Grip), Math.Min(2.5, c.Surface.Grip));
            var isHard = acc < -6 && ego.Speed > 0.5;
            if (isHard && !wasHard) hard++;
            wasHard = isHard;
            accs.Enqueue(acc); if (accs.Count > 6) accs.Dequeue();
            if (accs.Count == 6) maxJerk = Math.Max(maxJerk, Math.Abs(accs.Last() - accs.First()) / 0.5);
            double v1 = Math.Max(0, ego.Speed + acc * Dt);
            if (acc <= 0 && v1 < 0.1) v1 = 0;
            double ds = (ego.Speed + v1) / 2 * Dt;
            var path = decision.Chosen.Path;
            if (ds > 1e-6)
            {
                var (s0, _) = path.Project(ego.Position);
                var lateral = v1 * v1 * Math.Abs(path.CurvatureAt(s0 + ds));
                if (lateral > c.Surface.Grip) gripExceeded += Dt;
                ego.Position = path.At(s0 + ds); ego.Heading = path.HeadingAt(s0 + ds);
            }
            ego.Speed = v1;

            foreach (var a in sc.Agents) a.Step(world, Dt);

            // ---- THE TRUTH: gaps, time to collision, collisions ----
            var egoV = Vec2.FromHeading(ego.Heading) * ego.Speed;
            foreach (var a in sc.Agents)
            {
                if (a.Kind == RoadUserKind.Pedestrian && !a.Started) continue;
                var gap = Discs.Gap(ego.Position, ego.Heading, ego.Length, ego.Width, a.Position, a.Heading, a.Length, a.Width);
                minGap = Math.Min(minGap, gap);
                var r = a.Position - ego.Position; var rel = Vec2.FromHeading(a.Heading) * a.V - egoV;
                var closing = -rel.Dot(r.Unit);
                if (closing > 0.1 && gap < 50) minTtc = Math.Min(minTtc, Math.Max(0, gap) / closing);
                if (gap <= 0.01 && !collision) { collision = true; collisionKind = a.Kind.ToString(); collisionSpeed = ego.Speed; }
            }
            if (collision) { result = ego.Speed < 0.1 ? "collision (the CAV stopped, hit by another)" : "COLLISION"; break; }
        }

        var time = Math.Round(world.Time, 1);
        var stoppedShort = result.StartsWith("stopped");
        var closeCall = !collision && (minTtc < 1.0 || minGap < 1.0);
        var harm = Harm(collision, collisionKind, collisionSpeed, closeCall, hard, stoppedShort, time, freeFlow, gripExceeded);
        return new RunResult(sc.Context, sc.Variant, c.Motion, c.Surface.Name, c.Range, c.Noise, c.Seed, c.Injection,
            result, collision, collisionKind, Math.Round(collisionSpeed, 2), closeCall, Math.Round(minGap == double.MaxValue ? -1 : minGap, 2),
            Math.Round(minTtc == double.MaxValue ? -1 : minTtc, 2), hard, emergencies, stoppedShort, time, Math.Round(freeFlow, 1), Math.Round(maxJerk, 1),
            Math.Round(gripExceeded, 1), Math.Round(harm, 2), injected, Math.Round(clock.Elapsed.TotalSeconds, 2));
    }

    // THE HARM SCORE (written so that it can be argued; the weights are the night's choice, not a standard):
    //   a collision with a pedestrian or a cyclist: 100 x (1 + impact speed in m/s); with a car, a truck or an obstacle: 20 x (1 + impact speed);
    //     a CAV standing still that is hit by another: half of that (it could not have done more than stop);
    //   a close call (time to collision under 1 s, or a gap under 1 m, no collision): 5;
    //   a hard braking (deceleration over 6 m/s2, each episode): 1;
    //   seconds beyond the grip in a curve (the vehicle would skid): 10 a second;
    //   stopping short of the destination: 0.2 a second of the time lost (the run's time limit less the free-flow time); arriving late: 0.05 a second over 1.5 x free flow.
    public static double Harm(bool collision, string? kind, double v, bool closeCall, int hard, bool stoppedShort, double time, double freeFlow, double gripSeconds)
    {
        double h = 0;
        if (collision) h += (kind is "Pedestrian" or "Cyclist" ? 100 : 20) * (1 + v) * (v < 0.1 ? 0.5 : 1);
        if (closeCall) h += 5;
        h += hard;
        h += 10 * gripSeconds;
        if (stoppedShort) h += 0.2 * Math.Max(0, time - freeFlow);
        else if (!collision) h += 0.05 * Math.Max(0, time - 1.5 * freeFlow);
        return h;
    }

    // A PHANTOM: a pedestrian standing on the pavement beside the CAV's lane 20 m ahead; a car in the next lane (or the oncoming one) 15 m ahead going at
    // the CAV's speed; an obstacle in the CAV's lane 35 m ahead. Presented as the scene description gives an established road user (seen for 1 s).
    public static DescribedRoadUser? Phantom(string kind, Scene scene, RoadMap map)
    {
        var ego = scene.Ego;
        if (ego.LaneId is not { } laneId || !map.Lanes.TryGetValue(laneId, out var lane)) return null;
        var s = ego.S;
        DescribedRoadUser Make(RoadUserKind k, Vec2 p, double heading, double speed, double len, double wid, string? lid, double ls, double d) =>
            new(9000 + (int)k, k, p, Vec2.FromHeading(heading) * speed, heading, 0, len, wid, lid, ls, d, 0, 0, speed < 0.1, 1.0);
        if (kind.StartsWith("phantom-pedestrian"))
        {
            var ps = Math.Min(lane.Centre.Length, s + 20); var d = -(lane.Width / 2 + 1.5);
            return Make(RoadUserKind.Pedestrian, lane.Centre.At(ps, d), lane.Centre.HeadingAt(ps) + Math.PI / 2, 0, 0.5, 0.5, null, ps, d);
        }
        if (kind.StartsWith("phantom-car"))
        {
            var otherId = lane.Left ?? lane.Oncoming;
            if (otherId is null || !map.Lanes.TryGetValue(otherId, out var other)) return null;
            var p = lane.Centre.At(Math.Min(lane.Centre.Length, s + 15));
            var (os, _) = other.Centre.Project(p);
            var oncoming = lane.Left is null;
            return Make(RoadUserKind.Car, other.Centre.At(os), other.Centre.HeadingAt(os), oncoming ? Math.Max(8, ego.Speed) : ego.Speed, 4.5, 1.8, other.Id, os, 0);
        }
        if (kind.StartsWith("phantom-obstacle"))
        {
            var ps = Math.Min(lane.Centre.Length, s + 35);
            return Make(RoadUserKind.Obstacle, lane.Centre.At(ps), lane.Centre.HeadingAt(ps), 0, 1.0, 1.0, lane.Id, ps, 0);
        }
        return null;
    }
}
