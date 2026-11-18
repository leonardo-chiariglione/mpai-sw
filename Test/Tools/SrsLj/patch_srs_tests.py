"""N-27: the tests of the network Speech Model, by ID (DataID) instead of by file address."""
from pathlib import Path

P = Path(r"D:\AI\Test\Mpai.Aif.Tests\MasSrsAppTests.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)


rep("""    // SPEECH MODEL CREATION DONE OFFLINE: Speech Segments for Modelling named as a network of the folder are given that network, by reference (DataURI, DataLength),
    // in an ML Model Object valid against its schema; segments of another name are modelled as the deployment says (here the stand-in); a name with no network is
    // refused by the network trainer alone.""",
"""    // SPEECH MODEL CREATION DONE OFFLINE: Speech Segments for Modelling named as a network of the folder are given that network, named by its ID (MLModelData DataID:
    // model data of the deployment - no AIM opens a file named in data), in an ML Model Object valid against its schema; the Text To Speech finds the network by that ID
    // in its own folder of networks; segments of another name are modelled as the deployment says (here the stand-in); a name with no network is refused.""")
rep("""            var data = JsonNode.Parse(json)!["MLModelData"]![0]!;
            Assert.Equal(1234, data["DataLength"]!.GetValue<long>());
            Assert.Equal(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx"), new Uri(data["DataURI"]!.GetValue<string>()).LocalPath);
            Assert.Equal(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx"), Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(json));      // the Text To Speech finds it""",
"""            Assert.Equal("LJ-05min", (string)JsonNode.Parse(json)!["MLModelData"]![0]!["DataID"]!);
            Assert.Equal("LJ-05min", Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(json));
            Assert.Equal(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx"),                                                     // the Text To Speech finds it in its folder
                         Mpai.Aims.Tts.TtsFactory.NetworkFile(new Dictionary<string, string> { ["SpeechNetworks"] = folder }, "LJ-05min"));""")
i = s.index("    // The Text To Speech speaks with a network only from a file of this machine")
j = s.index("}\n", s.index("finally { File.Delete(file); File.Delete(file + \".json\"); }", i)) + 2
s = s[:i] + """    // The Text To Speech takes a network only by a plain name, of a network its folder holds; a model in its Data is not a network.
    [Fact]
    public void TheTextToSpeechTakesOnlyANetworkOfItsFolderByName()
    {
        string Model(string id) => new JsonObject { ["Header"] = "AIP-MLO-V1.0", ["MLModelObjectID"] = "m", ["MLModelData"] = new JsonArray(new JsonObject { ["DataID"] = id }) }.ToJsonString();
        Assert.Equal("LJ-60min", Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(Model("LJ-60min")));
        Assert.Throws<FormatException>(() => Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(Model("../../secret")));
        Assert.Throws<FormatException>(() => Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(\"\"\"{ "Header": "AIP-MLO-V1.0", "MLModelObjectID": "m", "MLModelData": [ { "Data": "AAAA" } ] }\"\"\"));
        Assert.Throws<InvalidOperationException>(() => Mpai.Aims.Tts.TtsFactory.NetworkFile(new Dictionary<string, string> { ["SpeechNetworks"] = Path.GetTempPath() }, "LJ-99min"));
    }
""" + s[j:]
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
