"""N-27 (night 2026-11-18): the network of the Speech Model by its ID (MLModelData {DataID}), not by a file address (DataURI): no AIM opens a file named in data (AIM-5).
The Text To Speech finds the network by that ID in its own folder of networks (setting SpeechNetworks, Models/SRS-LJ by default)."""
from pathlib import Path


def edit(path, pairs):
    p = Path(path)
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (p.name, old[:70], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit(r"D:\AI\AIMs\CAE1\V2.5\SMC\ISpeechModelTrainer.cs", [(
"""// A trained model: its weights (32-bit floats), how it was made, and from how many segments and how many seconds of speech. A model too large to travel in the Object
// (a whole network) travels by reference: DataUri and DataLength (the MLModelData form {DataLength, DataURI} of AIP-MLO-V1.0), and Weights is empty.
public sealed record TrainedSpeechModel(float[] Weights, string Method, int SegmentCount, double Seconds, string? DataUri = null, long DataLength = 0);""",
"""// A trained model: its weights (32-bit floats), how it was made, and from how many segments and how many seconds of speech. A model that is model data the deployment holds
// (a whole network trained offline) is named by its ID (the MLModelData form {DataID} of AIP-MLO-V1.0), and Weights is empty.
public sealed record TrainedSpeechModel(float[] Weights, string Method, int SegmentCount, double Seconds, string? DataId = null);""")])

edit(r"D:\AI\AIMs\CAE1\V2.5\SMC\SmcAimProcessor.cs", [(
"""            ["MLModelData"] = model.DataUri is { } uri
                ? new JsonArray(new JsonObject { ["DataLength"] = model.DataLength, ["DataURI"] = uri })
                : new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(bytes) }),""",
"""            ["MLModelData"] = model.DataId is { } dataId
                ? new JsonArray(new JsonObject { ["DataID"] = dataId })
                : new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(bytes) }),""")])

edit(r"D:\AI\AIMs\CAE1\V2.5\Voice\NetworkSpeechTrainer.cs", [
("""// as the set is (for instance LJ-05min: five minutes of LJ Speech): <folder>\\<name>\\<name>.onnx and <name>.onnx.json, with times.txt saying how it was trained. Given the Speech Segments
// for Modelling whose Speech Object has that ID, this trainer gives that network as the Neural Network Speech Model. A network is some 60 MB: it travels by reference (DataURI).""",
"""// as the set is (for instance LJ-05min: five minutes of LJ Speech): <folder>\\<name>\\<name>.onnx and <name>.onnx.json, with times.txt saying how it was trained. Given the Speech Segments
// for Modelling whose Speech Object has that ID, this trainer gives that network as the Neural Network Speech Model, named by its ID (MLModelData DataID): model data of the deployment,
// which the Text To Speech that speaks with it holds in its own folder of networks (no AIM opens a file named in data)."""),
("""        var file = Path.Combine(folder, segmentsId, segmentsId + ".onnx");
        var record = Path.Combine(folder, segmentsId, "times.txt");
        var how = File.Exists(record) ? " (" + File.ReadLines(record).First().Trim() + ")" : "";
        return new TrainedSpeechModel([], $"{MethodName}, network {segmentsId}{how}", segments.Count, segments.Sum(s => s.Seconds),
                                      new Uri(Path.GetFullPath(file)).AbsoluteUri, new FileInfo(file).Length);""",
"""        var record = Path.Combine(folder, segmentsId, "times.txt");
        var how = File.Exists(record) ? " (" + File.ReadLines(record).First().Trim() + ")" : "";
        return new TrainedSpeechModel([], $"{MethodName}, network {segmentsId}{how}", segments.Count, segments.Sum(s => s.Seconds), segmentsId);"""),
])

edit(r"D:\AI\AIMs\MMC\V2.5\TTS\TtsFactory.cs", [(
"""            // A NETWORK GIVEN AS THE SPEECH MODEL (a Piper/VITS voice made from the
            // speech of a speaker) is spoken with the same piper program.
            NetworkVoice = path => BuildVoice(runner, path, path + ".json")""",
"""            // A NETWORK GIVEN AS THE SPEECH MODEL (a Piper/VITS voice made from the
            // speech of a speaker), named by its ID: <SpeechNetworks>\\<id>\\<id>.onnx, the
            // folder of networks of this deployment (Models/SRS-LJ by default), spoken
            // with the same piper program.
            NetworkVoice = id => BuildVoice(runner, NetworkFile(settings, id), NetworkFile(settings, id) + ".json")""")])

edit(r"D:\AI\AIMs\MMC\V2.5\TTS\TtsFactory.cs", [(
"""    private static PiperVoice BuildVoice(""",
"""    // The network of a Speech Model by its ID, in the folder of networks the setting SpeechNetworks names. An ID that is not a plain name, or names no network, is refused.
    public static string NetworkFile(IReadOnlyDictionary<string, string> settings, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException($"'{id}' is not the name of a network.");
        var folder = Mpai.Core.MpaiPaths.Resolve(settings.TryGetValue("SpeechNetworks", out var f) && !string.IsNullOrWhiteSpace(f) ? f : "Models/SRS-LJ");
        var file = Path.Combine(folder, id, id + ".onnx");
        if (!File.Exists(file) || !File.Exists(file + ".json")) throw new InvalidOperationException($"This deployment has no network {id} (in {folder}).");
        return file;
    }

    private static PiperVoice BuildVoice(""")])

edit(r"D:\AI\AIMs\MMC\V2.5\TTS\PiperTtsAim.cs", [(
"""    public async Task<byte[]> SpeakWithNetworkAsync(string network, string text)
    {
        var voice = _networks.GetOrAdd(network, path =>""",
"""    public async Task<byte[]> SpeakWithNetworkAsync(string network, string text)
    {
        var voice = _networks.GetOrAdd(network, id =>""")])
edit(r"D:\AI\AIMs\MMC\V2.5\TTS\PiperTtsAim.cs", [(
"""(NetworkVoice ?? throw new InvalidOperationException("This Text To Speech cannot speak with a network."))(path));""",
"""(NetworkVoice ?? throw new InvalidOperationException("This Text To Speech cannot speak with a network."))(id));""")])

edit(r"D:\AI\AIMs\MMC\V2.5\TTS\TtsAimProcessor.cs", [
("""        // A SPEECH MODEL THAT IS A NETWORK (its data by reference, DataURI): each text of the Text Object said with it, one
        // speech file for each (the Speech Restoration System puts each in its place). A Speech Model of another kind is not
        // used, as before: this Text To Speech speaks in its own voice.
        if (_modelPort.Length > 0 && message.Ports.TryGetValue(_modelPort, out var modelJson) && !string.IsNullOrWhiteSpace(modelJson) && modelJson.Contains("\\"DataURI\\"", StringComparison.Ordinal))
        {
            try { return await SpeakWithNetworkAsync(message, NetworkOf(modelJson)); }""",
"""        // A SPEECH MODEL THAT IS A NETWORK OF THE DEPLOYMENT (named by its ID, MLModelData DataID): each text of the Text Object said
        // with it, one speech file for each (the Speech Restoration System puts each in its place). A Speech Model of another kind is
        // not used, as before: this Text To Speech speaks in its own voice.
        if (_modelPort.Length > 0 && message.Ports.TryGetValue(_modelPort, out var modelJson) && !string.IsNullOrWhiteSpace(modelJson) && modelJson.Contains("\\"DataID\\"", StringComparison.Ordinal))
        {
            try { return await SpeakWithNetworkAsync(message, NetworkOf(modelJson)); }"""),
("""    // The file of the network an ML Model Object (AIP-MLO-V1.0) points to: MLModelData {DataLength, DataURI}, a file: URI of this machine, the voice's
    // .onnx with its .onnx.json beside it. A Speech Model that is not a network (numbers in its Data) is refused: this Text To Speech cannot speak with it.
    // Fetching a payload that is not on this machine is the Controller's (MPAI_AIFM_Payload_Get), not done here.
    public static string NetworkOf(string modelJson)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(modelJson) as JsonObject; }
        catch (System.Text.Json.JsonException ex) { throw new FormatException("The Speech Model is not JSON: " + ex.Message); }
        var item = (root?["MLModelData"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => o["DataURI"] is not null)
                   ?? throw new FormatException("The Speech Model is not a network (it has no DataURI): this Text To Speech cannot speak with it.");
        if (!Uri.TryCreate(item["DataURI"]!.GetValue<string>(), UriKind.Absolute, out var uri) || !uri.IsFile)
            throw new FormatException($"The network of the Speech Model is at {item["DataURI"]}: this Implementation reads a network only from a file of this machine.");
        var path = uri.LocalPath;
        if (!File.Exists(path) || !File.Exists(path + ".json")) throw new FormatException($"The network {path} (or its {Path.GetFileName(path)}.json) does not exist.");
        if (item["DataLength"] is JsonValue length && length.GetValue<long>() != new FileInfo(path).Length)
            throw new FormatException($"The network {path} is not the one the Speech Model describes (DataLength {length}, the file has {new FileInfo(path).Length} bytes).");
        return path;
    }""",
"""    // The ID of the network an ML Model Object (AIP-MLO-V1.0) names: MLModelData {DataID}. The network itself is model data of this deployment (TtsFactory.NetworkFile):
    // no file named in the data is opened. A Speech Model that is not a network (numbers in its Data) is refused: this Text To Speech cannot speak with it.
    public static string NetworkOf(string modelJson)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(modelJson) as JsonObject; }
        catch (System.Text.Json.JsonException ex) { throw new FormatException("The Speech Model is not JSON: " + ex.Message); }
        var item = (root?["MLModelData"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => o["DataID"] is not null)
                   ?? throw new FormatException("The Speech Model is not a network of this deployment (it has no DataID): this Text To Speech cannot speak with it.");
        var id = item["DataID"]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
            throw new FormatException($"'{id}' is not the name of a network.");
        return id;
    }"""),
])
print("ok")
