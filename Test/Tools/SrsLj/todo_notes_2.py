"""Night 2026-11-18: N-31 doing, with its note."""
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


status("### N-31 ", "doing (2026-11-18 night: the word alignment done, Fast tests pass; the Ollama tests wait for the GPU, see the note)")
note_before_next("### N-31 ", "NOTE 2026-11-18 01:50 (night): found already done (by N-37 and by day): no PAF-PDX in the L2 or the L3 of 1PGM-ATU-V1.0-I01; the Process Actions go to the reference M-Instance through the MMM-API (MmmClient, MMM\\Server\\DoorUseCase.cs); the tests asked for exist (PgmLevelsTests: Level 0, 1, 2 by hand, a lower Level, a Required function Not Realised, conformance, closed lists). Built tonight: the word times of Behaviour Planning are ALIGNED WITH THE MACHINE SPEECH (AIMs\\PGM\\V1.0\\Communicative.cs: SamplesOf, WordTimes(text, samples, rate): silences before/after given to no word, each pause of 120 ms or more put at a word boundary, letters shared within each stretch; the duration-only sharing stays where the speech cannot be read). Tests: PgmWordAlignmentTests (2, Fast) pass; PgmWordAlignmentModelTests (Piper amy + Whisper word times on 6 sentences, 58 words): mean distance of the word starts from Whisper's 0.124 s aligned against 0.157 s by letters alone; all PGM Fast tests 36/36. Left: the Ollama tests (PgmConditionOllamaTests, PgmControllerTests with Ollama, PgmLevelsDemo) after the N-27 training frees the GPU; \"a real voice in the demo\": the door demo has the Piper voice since N-37 - question: is the BKN Levels page (PgmLevelsDemo) meant to have a voice too?")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
