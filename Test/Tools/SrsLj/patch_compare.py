"""N-34 step 4 (night 2026-11-18): compare_who_speaks.py takes the programme times of the MEV as they are, and checks the position of each utterance."""
from pathlib import Path

P = Path(r"D:\AI\Test\Data\TMA\compare_who_speaks.py")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:60]
    s = s.replace(old, new)


rep('''base = min(s["BMSDescriptorsSpaceTime"]["Time"]["SimpleTimeData"][0]["StartTime"] for s in speech)   # the wall clock AUS started from''',
    '''first = min(s["BMSDescriptorsSpaceTime"]["Time"]["SimpleTimeData"][0]["StartTime"] for s in speech)
base = first if first > 1e6 else 0.0   # programme time (relative, since 2026-11-15) is taken as it is; a wall-clock time from where AUS started''')
s = s.rstrip("\n") + '''

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
'''
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
