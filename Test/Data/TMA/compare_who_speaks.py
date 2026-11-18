"""Compare what TMA's Speaker Identity Recognition said for each speech segment with the AMI reference who-speaks-when.
Usage: tma_compare.py <aim-outputs.txt> <last-mev.json> <reference.csv>"""
import csv, json, sys, collections

outs, mev, ref = sys.argv[1], sys.argv[2], sys.argv[3]
lines = [l.rstrip("\n").split(" | ", 2) for l in open(outs, encoding="utf-8") if l.count(" | ") >= 2]
scenes = json.load(open(mev, encoding="utf-8"))["MultimodalSceneDescriptors"]
speech = [s for s in scenes if "BasicSpeechSceneDescriptors" in json.dumps(s.get("BasicMultimodalSceneDescriptorsData", ""))]
first = min(s["BMSDescriptorsSpaceTime"]["Time"]["SimpleTimeData"][0]["StartTime"] for s in speech)
base = first if first > 1e6 else 0.0   # programme time (relative, since 2026-11-15) is taken as it is; a wall-clock time from where AUS started

stm = next(json.loads(j) for a, p, j in lines if "AUS" in a and p.startswith("OSD-STM") and j.startswith("{"))
starts = [d["StartTime"] - base for d in stm["SimpleTimeData"]]
durs = [(starts[i + 1] if i + 1 < len(starts) else 300.0) - starts[i] for i in range(len(starts))]   # a segment runs to the next boundary
labels = []
for a, p, j in lines:
    if "SIR" in a and p.startswith("OSD-IID") and j.startswith("{"):
        d = json.loads(j)["InstanceIdentifierData"][0]
        labels.append((d["InstanceLabel"], d.get("LabelConfidenceLevel")))
texts = [json.loads(j).get("BasicTextData", [{}])[0].get("Data", "") for a, p, j in lines if "ASR" in a and p.startswith("OSD-BTO") and j.startswith("{")]

reference = [(r["speaker"], float(r["start_s"]), float(r["end_s"])) for r in csv.DictReader(open(ref, encoding="utf-8"))]
def truth(a, b):
    t = collections.Counter()
    for s, x, y in reference: t[s] += max(0.0, min(b, y) - max(a, x))
    return t

print(f"segments: {len(starts)} starts, {len(durs)} objects, {len(labels)} SIR answers, {len(texts)} ASR texts")
rows, right, timed = [], 0, 0
for i, (a, d) in enumerate(zip(starts, durs)):
    b = a + d
    t = truth(a, b); main, share = (t.most_common(1)[0] if t else ("-", 0.0))
    lab, conf = labels[i] if i < len(labels) else ("?", None)
    ok = lab == main
    right += ok * d; timed += d
    rows.append((i + 1, a, b, main, round(share / d, 2) if d else 0, lab, conf, ok, texts[i][:70] if i < len(texts) else ""))
    print(f"{i+1:2d} {a:6.1f}-{b:6.1f}s  reference {main} ({share/d*100 if d else 0:3.0f}% of the segment)  SIR {lab:<8} conf {conf}  {'RIGHT' if ok else 'wrong'}  | {rows[-1][-1]}")
print(f"speech time attributed to the right speaker: {right:.1f} s of {timed:.1f} s = {100*right/timed if timed else 0:.0f}%")
print("SIR labels used:", collections.Counter(l for l, _ in labels))

# WHERE (2026-11-18): the position of each utterance (the face of its speaker, in pixels of the 960x540 picture) against where the reference speaker sits:
# the left camera is the left half (A on its left, C on its right), the right camera the right half (D on its left, B on its right).
seat = {"A": (0, 240), "C": (240, 480), "D": (480, 720), "B": (720, 960)}
placed = right_place = 0
faces = collections.Counter()
for sc in scenes:
    for o in sc.get("BasicObjectsData", []):
        v = o.get("BXOOrBXOID", {})
        if v.get("VisualQualifier", {}).get("Attributes", {}).get("VisualObjectType") == "Face":
            faces[v["VisualQualifier"]["Attributes"]["ObjectID"]["InstanceIdentifierData"][0]["InstanceLabel"]] += 1
for sc in speech:
    for item in sc.get("BasicMultimodalSceneDescriptorsData", []):
        for d in item.get("BXSOrBXSID", {}).get("BasicSpeechSceneDescriptors", []):
            for o in d.get("ObjectIDOrObject", []):
                if not isinstance(o, dict):
                    continue
                st = o.get("BasicSpeechObjectSpaceTime", {})
                sa = st.get("SpatialAttitude1")
                if not sa:
                    continue
                t = (o.get("BasicSpeechObjectTime") or st.get("Time"))["SimpleTimeData"][0]
                a, b = t["StartTime"] - base, t["EndTime"] - base
                main = truth(a, b).most_common(1)
                x = sa["Position"]["CartPosition"][0]
                placed += 1
                if main and main[0][0] in seat and seat[main[0][0]][0] <= x < seat[main[0][0]][1]:
                    right_place += 1
print(f"faces in the MEV, by label: {dict(faces)}")
print(f"utterances with a position: {placed}; at the seat of the reference speaker: {right_place}")
