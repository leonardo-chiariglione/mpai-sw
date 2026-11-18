"""Night 2026-11-18: N-34 done, with its note."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def status(head, new):
    global s
    i = s.index(head); j = s.index("Status: ", i); k = s.index("\n", j)
    s = s[:j] + "Status: " + new + s[k:]


def note_before_next(head, text):
    global s
    i = s.index(head); j = s.index("\n### ", i + len(head))
    s = s[:j].rstrip("\n") + "\n" + text + "\n" + s[j:]


status("### N-34 ", "done (2026-11-18 night: the meeting's MEV is valid and carries who, what, where and when; see the note)")
note_before_next("### N-34 ", """NOTE 2026-11-18 (night): DONE. Nothing committed; no schema and no L2 changed.
- Step 3 POSITION: CVE-VCD gives each scene its programme time (relative) and, with the setting SampleSeconds (1 in TMA), one frame a second (BasicVisualObjectProperties.FrameTimes); new AIMs\\PAF\\V1.6\\FIR\\FirEachFace.cs = FIR for TMA (1PAF-FIR-V1.6-I02): every face of every sampled frame as an Instance Identifier with InstanceTime and InstanceSpaceTime (Spatial Attitude: centre of the box in pixels, accuracy half the box; Spatial Qualifier Screen / top-left / pixel); a face the gallery does not name goes to the registered person it is most like (>= 0.25, 0.08 above the next), otherwise a stable label unregistered-N; OSD-MSM (TmaAim.cs) records each face as a Basic Visual Object (Face, ObjectID = the IID) of the Visual Scene of its time, places an utterance where its speaker's face is (BasicSpeechObjectSpaceTime.SpatialAttitude1) and records the pair in AlignedMMObjects (SpeakerFace, Confidence = voice x face); IDR confirms a voice by a face of the same person seen during the utterance. The FIR Bounding Box Port is not connected in the TMA L2: not needed (the IID carries the place) - no Topology change.
- Defects found and fixed on the way: (1) the MEV was not valid (the 3 TMA reds of the 2026-11-17 suite): the Text Qualifier of the words (typed model of Mpai.Core) writes SubType/Format where the schema wants SubTypes/Formats - MSM now records the words under the schema's names; the model itself is a defect for the author (MKG\\pending-changes.md); (2) MSM read the visual scene's time under a wrong name, and BVS dropped the picture's time (the visual codec does not carry it): fixed; (3) the TMA test gave FIR Models\\glintr100.onnx, a different file from the face model the L3 pins (SHA-256 4AB1D643..., the one ACR registers with): no face could match A-D; the test now gives FIR the pinned model where it is on this machine (AIF\\Access); (4) compare_who_speaks.py subtracted the first utterance's time (wall-clock era); it now takes programme times as they are and checks positions against the seats.
- Step 4, TheAmiMeetingWithItsParticipantsRegistered, 300 s (Test\\Results\\2026-11-15\\tma-ami-registered.txt, last MEV and AIM outputs beside it): passes, MEV valid, 0 AIM errors; 18 scenes (17 utterances, 1 visual scene); every utterance with its programme time and its words; SpeakerIdentity on 17 (9 named A-D, 8 'speech' = not identified); faces A 300, B 292, C 283, D 254 + 65 unmatched; 9 utterances with a position = the 9 whose speaker was named, all 9 at the seat of the reference speaker; speech time to the right speaker 80% (unchanged).
- Tests: TmaWhereTests (4, Fast) and TmaFaceTests (1, Models, real faces) new, TmaTests pass (14 with them); MKG\\pending-changes.md notes. Step 2 (the whole Fast group) is in the whole run at the end of the night.""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
