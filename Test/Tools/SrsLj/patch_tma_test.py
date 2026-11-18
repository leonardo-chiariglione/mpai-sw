"""N-34 step 3 (night 2026-11-18): the TMA test provider uses FIR for TMA (every face, frame by frame); VCD samples one frame a second; the AMI test reports faces and positions."""
from pathlib import Path

P = Path(r"D:\AI\Test\Mpai.Aif.Tests\TmaEndToEndTests.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)


rep("""                "1PAF-FIR-V1.6-I02" => new Mpai.Paf.Fir.FirAimProcessor(aimName, Scrfd(settings), ArcFace(settings), _gallery, ports),""",
    """                "1PAF-FIR-V1.6-I02" => new Mpai.Paf.Fir.FirEachFace(aimName, Scrfd(settings), ArcFace(settings), _gallery),     // every face of every sampled frame, where and when""")
rep("""            return aim is TmaAim ? aim : new Recorder(aim, _log);""",
    """            return aim is TmaAim or Mpai.Paf.Fir.FirEachFace ? aim : new Recorder(aim, _log);""")
rep("""            ["1CVE-VCD-V1.0-I01"] = new JsonObject { ["ImagesPerScene"] = "1", ["MinSceneLength"] = "15" }""",
    """            ["1CVE-VCD-V1.0-I01"] = new JsonObject { ["ImagesPerScene"] = "1", ["MinSceneLength"] = "15", ["SampleSeconds"] = "1" }""")
rep("""        AmiRecord($"faces given by FIR: {data.Count(l => l.Aim.Contains("FIR") && l.Port.Contains("OSD-IID"))}; voices given by SIR: {data.Count(l => l.Aim.Contains("SIR") && l.Port.Contains("OSD-IID"))}");""",
    """        AmiRecord($"voices given by SIR: {data.Count(l => l.Aim.Contains("SIR") && l.Port.Contains("OSD-IID"))}");
        // WHO, WHAT, WHERE AND WHEN in the last MEV (2026-11-18): the faces recorded in the Visual Scenes, the utterances with their time, speaker, words and position.
        var faceObjects = scenes.SelectMany(x => x!["BasicObjectsData"] as JsonArray ?? []).Select(o => o?["BXOOrBXOID"])
                                .Where(o => (string?)o?["VisualQualifier"]?["Attributes"]?["VisualObjectType"] == "Face").ToList();
        AmiRecord($"faces in the MEV: {faceObjects.Count}; by person: " + string.Join(", ", faceObjects.GroupBy(o => (string?)o!["VisualQualifier"]!["Attributes"]!["ObjectID"]!["InstanceIdentifierData"]![0]!["InstanceLabel"])
                                                                                     .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        var utterances = scenes.Select(x => x!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"]?[0]?["ObjectIDOrObject"]?[0]).Where(o => o is not null).ToList();
        AmiRecord($"utterances: {utterances.Count}; with their time: {utterances.Count(u => u!["BasicSpeechObjectTime"] is not null || u["BasicSpeechObjectSpaceTime"]?["Time"] is not null)}; " +
                  $"with a speaker: {utterances.Count(u => u!["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["SpeakerIdentity"] is not null)}; " +
                  $"with their words: {utterances.Count(u => u!["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["ContentDescription"]?["TextObject"] is not null)}; " +
                  $"with a position: {utterances.Count(u => u!["BasicSpeechObjectSpaceTime"]?["SpatialAttitude1"] is not null)}; " +
                  $"voice-face alignments: {scenes.Sum(x => (x!["AlignedMMObjects"] as JsonArray)?.Count ?? 0)}");""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
