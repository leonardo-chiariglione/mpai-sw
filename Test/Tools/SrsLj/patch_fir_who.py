"""N-34 (night 2026-11-18): FIR of TMA - small faces of a programme are matched to the registered people by the best and the second best likeness (closed set),
and an unregistered face is compared with every face seen under a label."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\PAF\V1.6\FIR\FirEachFace.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)


rep("""    public const double UnregisteredLikeness = 0.45;
""",
"""    public const double UnregisteredLikeness = 0.35, RegisteredLikeness = 0.25, RegisteredMargin = 0.08;
    // A FACE OF A PROGRAMME IS SMALL (in the AMI meeting at 960x540, 30 to 50 pixels wide): its likeness to the picture a person was registered from is lower than the gallery's
    // threshold (0.35) even when it is that person (measured 2026-11-18 on two frames: 0.25 to 0.61, always the highest for the right person). A face the gallery does not name
    // is therefore given to the registered person it is most like when that likeness is RegisteredLikeness or more AND it exceeds the next person's by RegisteredMargin.
""")
rep("""    private readonly List<(string Label, float[] Embedding)> _unregistered = [];""",
    """    private readonly List<(string Label, float[] Embedding)> _unregistered = [];          // every face seen under each unregistered label (up to 20 a label)""")
rep("""        if (_gallery.IdentifyFace(embedding) is GalleryMatch m) return (m.SubjectId, m.Similarity, true);
        var best = _unregistered.Select(u => (u.Label, Likeness: (double)ArcFaceRecogniser.CosineSimilarity(u.Embedding, embedding))).OrderByDescending(u => u.Likeness).FirstOrDefault();
        if (best.Label is not null && best.Likeness >= UnregisteredLikeness) return (best.Label, best.Likeness, false);""",
"""        if (_gallery.IdentifyFace(embedding) is GalleryMatch m) return (m.SubjectId, m.Similarity, true);
        var registered = _gallery.SubjectIds.ToHashSet();
        var scores = _gallery.ScoreFace(embedding).Where(x => registered.Contains(x.SubjectId)).OrderByDescending(x => x.Score).ToList();
        if (scores.Count > 0 && scores[0].Score >= RegisteredLikeness && (scores.Count == 1 || scores[0].Score - scores[1].Score >= RegisteredMargin))
            return (scores[0].SubjectId, scores[0].Score, true);
        var best = _unregistered.GroupBy(u => u.Label).Select(g => (Label: g.Key, Likeness: g.Max(u => (double)ArcFaceRecogniser.CosineSimilarity(u.Embedding, embedding))))
                                .OrderByDescending(u => u.Likeness).FirstOrDefault();
        if (best.Label is not null && best.Likeness >= UnregisteredLikeness)
        {
            if (_unregistered.Count(u => u.Label == best.Label) < 20) _unregistered.Add((best.Label, embedding));
            return (best.Label, best.Likeness, false);
        }""")
rep("""        var label = "unregistered-" + (_unregistered.Count + 1);""",
    """        var label = "unregistered-" + (_unregistered.Select(u => u.Label).Distinct().Count() + 1);""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
