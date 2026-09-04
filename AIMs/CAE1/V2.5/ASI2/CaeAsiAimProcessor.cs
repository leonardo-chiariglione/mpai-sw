using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core;

namespace Mpai.Cae.Asi;

// CAE-ASI-V2.5 - Audio Scene object Identification (the scanner).
//
// Reads the Basic Audio Scene Descriptors and dispatches each object by its TYPE,
// which is the object's own schema: a Basic Speech Object (OSD-BSO) goes to
// Speaker Recognition, a generic Basic Audio Object (OSD-BAO) goes to Audio
// Instance Identification. Two different data types, so routing is by data type -
// speech to the speech identifier, sound to the audio identifier. No qualifier
// needed: the schema (the object's Header) is the type.
//
// The scene entry's object is read generically (its Header decides the route) and
// emitted verbatim, so a Speech Object is preserved exactly rather than being
// coerced into a generic Audio Object.
public sealed class CaeAsiAimProcessor : IAimProcessor
{
    private readonly string _instanceId;
    private readonly string _inPort;      // OSD-BAS
    private readonly string _speechPort;  // OSD-BSO -> SIR
    private readonly string _audioPort;   // OSD-BAO -> AII

    public CaeAsiAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId = instanceId;
        _inPort      = ports.Input("OSD-BAS-V1.5");
        _speechPort  = ports.Output("OSD-BSO-V1.5");
        _audioPort   = ports.Output("OSD-BAO-V1.5");
    }

    public string InstanceId => _instanceId;

    public Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var json) || string.IsNullOrWhiteSpace(json))
            return Task.FromResult(Message.Error(message.MessageId, _instanceId, "no Basic Audio Scene Descriptors on input port"));

        var ports = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // The scene descriptors carry an entries array; each entry has an
            // object under a "...ObjectIDOrA...Object" property. Find the first
            // entry's object, read its Header, route + emit verbatim.
            if (TryGetEntries(root, out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (!TryGetObject(entry, out var objEl)) continue;
                    var header = objEl.TryGetProperty("Header", out var h) ? (h.GetString() ?? "") : "";
                    var objJson = objEl.GetRawText();
                    if (header.StartsWith("OSD-BSO")) ports[_speechPort] = objJson;
                    else                              ports[_audioPort]  = objJson;
                    break;   // single-object scene: emit the first
                }
            }
        }
        catch { /* fall through to error below */ }

        if (ports.Count == 0)
            return Task.FromResult(Message.Error(message.MessageId, _instanceId, "no audio object in scene"));

        return Task.FromResult(new Message
        {
            MessageId   = message.MessageId,
            MessageType = message.MessageType,
            Ports       = ports
        });
    }

    private static bool TryGetEntries(JsonElement root, out JsonElement entries)
    {
        // property name in the data schema: BasicAudioSceneDescriptors (the array)
        foreach (var name in new[] { "BasicAudioSceneDescriptors", "BasicAudioSceneDescriptorsEntries", "AudioObjects" })
            if (root.TryGetProperty(name, out entries) && entries.ValueKind == JsonValueKind.Array) return true;
        entries = default; return false;
    }

    private static bool TryGetObject(JsonElement entry, out JsonElement obj)
    {
        foreach (var name in new[] { "AObjectIDOrAObject", "AudioObjectIDOrAudioObject", "ObjectIDOrObject" })
            if (entry.TryGetProperty(name, out obj) && obj.ValueKind == JsonValueKind.Object) return true;
        obj = default; return false;
    }
}
