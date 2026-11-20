using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Aif.PortData;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Osd.Voe;

// OSD-VOE-V1.5 - Visual Object Extraction.
//
// From the Visual Objects of a Visual Scene takes the Target Visual Object (OSD-BVO) that a Point of View (OSD-OPV) crosses in the Basic Visual Scene Descriptors
// (OSD-BVS; 2026-11-15, was the Visual Scene Geometry): the object whose place lies in the cone of the Point of View, closest to its axis, in front of it. The cone is the Aperture of the Point of
// View (twice the half-angle; 10 degrees if it has none). The Orientation is read as (azimuth, elevation, 0) in degrees in a frame x right, y down, z
// away, as the Visual Direction Identification writes it (see there; the standard does not fix it).
//
// A Port carries one Visual Object, so the AIM keeps the Visual Objects it has received by their identifier (a continuous run is one execution) and
// gives the one the Point of View crosses. The identifier of a member of the Basic Visual Scene Descriptors is the string that stands for its Visual Object,
// or the BasicVisualObjectID of the Visual Object it carries.
//
// NOTHING CROSSED IS NOT A FAULT (2026-11-20): the AIM keeps the latest Point of View and the latest Basic Visual Scene Descriptors. A Point of View that
// crosses no Visual Object gives no output and no error (an error would make the Controller stop the Module by default); one that crosses a Visual Object
// not yet received is kept, and the object is given when it arrives. A Point of View or Descriptors that cannot be read remain errors.
public sealed class OsdVoeAimProcessor : IAimProcessor
{
    private const double DefaultApertureDegrees = 10;
    private readonly string _instanceId;
    private readonly string _povPort, _geometryPort, _objectsPort, _outPort;
    private readonly Dictionary<string, string> _received = new();      // BasicVisualObjectID -> the object as it came
    private string? _pov, _geometry;                                      // the latest Point of View not yet answered, the latest Descriptors

    public OsdVoeAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId   = instanceId;
        _povPort      = ports.Input("OSD-OPV-V1.5");
        _geometryPort = ports.Input("OSD-BVS-V1.5");
        _objectsPort  = ports.Input("OSD-BVO-V1.5");
        _outPort      = ports.Output("OSD-BVO-V1.5");
    }

    public string InstanceId => _instanceId;

    public Task<Message> ProcessAsync(Message message)
    {
        if (message.Ports.TryGetValue(_objectsPort, out var objectJson) && !string.IsNullOrWhiteSpace(objectJson))
        {
            var visual = PortObjects.ReadVisual(objectJson);
            if (visual is not null) _received[visual.BasicVisualObjectID] = objectJson;
        }
        if (message.Ports.TryGetValue(_povPort, out var p) && !string.IsNullOrWhiteSpace(p)) _pov = p;
        if (message.Ports.TryGetValue(_geometryPort, out var g) && !string.IsNullOrWhiteSpace(g)) _geometry = g;
        if (_pov is not { } povJson || _geometry is not { } geometryJson) return Task.FromResult(Nothing(message));

        var pov = MpaiJson.FromJson<PointOfView>(povJson);
        if (pov is null || pov.CartPosition.Length != 3 || pov.Orientation.Length < 2)
        {
            _pov = null;
            return Task.FromResult(Message.Error(message.MessageId, _instanceId, "the Point of View cannot be read"));
        }
        if (JsonNode.Parse(geometryJson) is not JsonObject geometry)
            return Task.FromResult(Message.Error(message.MessageId, _instanceId, "the Basic Visual Scene Descriptors cannot be read"));

        var az = pov.Orientation[0] * Math.PI / 180; var el = pov.Orientation[1] * Math.PI / 180;
        double[] axis = { Math.Cos(el) * Math.Sin(az), -Math.Sin(el), Math.Cos(el) * Math.Cos(az) };
        var aperture = pov.Aperture is { Count: > 0 } a ? Math.Max(a[0].Azimuth, a[0].Elevation) : DefaultApertureDegrees;
        var half = aperture / 2;

        string? crossed = null; double bestAngle = double.MaxValue, bestDistance = double.MaxValue;
        foreach (var item in geometry["BasicVisualSceneDescriptors"] as JsonArray ?? new JsonArray())
        {
            var id = IdentifierOf(item);
            var place = (item?["VisualObjectSpaceTime"]?["SpatialAttitude1"]?["Position"]?["CartPosition"] as JsonArray)?.Select(n => n?.GetValue<double>() ?? 0).ToArray();
            if (id is null || place is null || place.Length != 3) continue;
            double[] v = { place[0] - pov.CartPosition[0], place[1] - pov.CartPosition[1], place[2] - pov.CartPosition[2] };
            var distance = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (distance < 1e-9) continue;
            var cos = (v[0] * axis[0] + v[1] * axis[1] + v[2] * axis[2]) / distance;
            if (cos <= 0) continue;                                                  // behind the Point of View
            var angle = Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
            if (angle > half) continue;
            if (angle < bestAngle || (angle == bestAngle && distance < bestDistance)) { crossed = id; bestAngle = angle; bestDistance = distance; }
        }
        if (crossed is null) { _pov = null; return Task.FromResult(Nothing(message)); }                    // the Point of View crosses no Visual Object
        if (!_received.TryGetValue(crossed, out var target)) return Task.FromResult(Nothing(message));    // kept until the object arrives
        _pov = null;
        return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = message.MessageType, Ports = new Dictionary<string, string> { [_outPort] = target } });
    }

    // No Target Visual Object: an answer with no output, not an error.
    private static Message Nothing(Message message) => new() { MessageId = message.MessageId, MessageType = message.MessageType, Ports = new Dictionary<string, string>() };

    // The identifier of a member of the Basic Visual Scene Descriptors: the string that stands for its Visual Object, or the BasicVisualObjectID of the
    // Visual Object it carries.
    private static string? IdentifierOf(JsonNode? item)
    {
        var one = item?["VObjectIDOrVObject"];
        if (one is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return one?["BasicVisualObjectID"]?.GetValue<string>();
    }
}

public sealed class VoePlugin : IAimPlugin
{
    public string AimName => "OSD-VOE-V1.5";
    public IAimProcessor Create(AimPortReader ports, IReadOnlyDictionary<string, string> settings) => new OsdVoeAimProcessor(AimName, ports);
}
