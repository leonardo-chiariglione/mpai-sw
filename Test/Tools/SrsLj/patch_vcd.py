"""N-34 step 3 (night 2026-11-18): Visual Change Detection gives programme time, and samples the frames of a scene (setting SampleSeconds) with the time of each."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\OSD\V1.5\TMA\VisualChangeDetection.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:60]
    s = s.replace(old, new)


rep("""// changed frame is given, and, where ImagesPerScene (1; 5 in the reference software of V1.4) is more, frames spread over the scene after it, in one Basic Visual Object a scene.""",
"""// changed frame is given, and, where ImagesPerScene (1; 5 in the reference software of V1.4) is more, frames spread over the scene after it, in one Basic Visual Object a scene.
// Where SampleSeconds is given (Television Media Analysis: 1), the frames are taken through the whole scene, one every SampleSeconds, so that who is on the screen is seen as the
// scene goes on. THE TIME IS THE PROGRAMME'S (2026-11-18; the author: the MEV knows of no other time): a scene runs from its first frame to its last, in seconds from the first
// frame of the video (a relative Simple Time), and BasicVisualObjectProperties.FrameTimes gives the time of each frame given.""")
rep("""    private readonly int imagesPerScene = Math.Max(1, (int)Setting(settings, "ImagesPerScene", 1));""",
"""    private readonly int imagesPerScene = Math.Max(1, (int)Setting(settings, "ImagesPerScene", 1));
    private readonly double sampleSeconds = Setting(settings, "SampleSeconds", 0);""")
rep("""            var begin = DateTimeOffset.UtcNow;
""", "")
rep("""                var picked = new List<int> { from };
                for (var k = 1; k < imagesPerScene; k++)
                {
                    var f = from + (int)Math.Round(k * (to - from) / (double)imagesPerScene);
                    if (f > picked[^1] && f < to) picked.Add(f);
                }""",
"""                var picked = new List<int> { from };
                if (sampleSeconds > 0)
                    for (var f = from + (int)Math.Round(sampleSeconds * fps); f < to; f += Math.Max(1, (int)Math.Round(sampleSeconds * fps))) picked.Add(f);
                else
                    for (var k = 1; k < imagesPerScene; k++)
                    {
                        var f = from + (int)Math.Round(k * (to - from) / (double)imagesPerScene);
                        if (f > picked[^1] && f < to) picked.Add(f);
                    }""")
rep("""                    ["BasicVisualObjectTime"] = TmaTypes.SpaceTime(begin.AddSeconds(from / fps)),
                    ["BasicVisualObjectData"] = data,
                    ["BasicVisualObjectProperties"] = new JsonObject { ["FrameRate"] = fps, ["FirstFrame"] = from, ["Frames"] = to - from }""",
"""                    ["BasicVisualObjectTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = TmaTypes.NewId("spt"), ["Time"] = ProgrammeTime(from / fps, to / fps) },
                    ["BasicVisualObjectData"] = data,
                    ["BasicVisualObjectProperties"] = new JsonObject
                    {
                        ["FrameRate"] = fps, ["FirstFrame"] = from, ["Frames"] = to - from,
                        ["FrameTimes"] = new JsonArray(picked.Select(f => (JsonNode)Math.Round(f / fps, 3)).ToArray())
                    }""")
rep("""    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (json["BasicVisualObjectData"] is not JsonArray entries) yield break;""",
"""    // A stretch of the programme: from and to, in seconds from its start (a relative Simple Time).
    public static JsonObject ProgrammeTime(double from, double to) => new()
    {
        ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = TmaTypes.NewId("st"),
        ["SimpleTimeData"] = new JsonArray(new JsonObject
        {
            ["FlagsByte"] = 0, ["StartTime"] = Math.Round(from, 3), ["EndTime"] = Math.Round(to, 3), ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.0, ["TimeType"] = false, ["TimeUnit"] = "00"
        })
    };

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (json["BasicVisualObjectData"] is not JsonArray entries) yield break;""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
