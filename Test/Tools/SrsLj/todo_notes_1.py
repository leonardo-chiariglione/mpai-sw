"""Night 2026-11-18: notes under N-27 (doing) and N-28, N-29, N-30 (blocked) in Night\\TODO.md. Requests are not reworded; only Status lines change and notes are added."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def status(head, new):
    global s
    i = s.index(head)
    j = s.index("Status: ", i)
    k = s.index("\n", j)
    s = s[:j] + "Status: " + new + s[k:]


def note_before_next(head, text):
    global s
    i = s.index(head)
    j = s.index("\n### ", i + len(head))
    s = s[:j].rstrip("\n") + "\n" + text + "\n" + s[j:]


status("### N-27 ", "doing (2026-11-18 night: the five trainings run on the GPU; App and test material done; see the note)")
note_before_next("### N-27 ", "NOTE 2026-11-18 01:30 (night, in progress): sets chosen (Test\\Data\\Speech\\LJSpeech, README; 20 held-out, 10 validation, nested 1/5/15/30/60 min = 13/50/134/272/541 clips, seed 20261115); the five trainings queued on the GPU (Test\\Tools\\SrsLj\\train_all.sh, same budget 2,200 batches of 4 clips each, constant learning rate, base kristin-2000, fixed split); baseline LJ-00min (base not fine-tuned) exported and measured; damaged test material made (two sections of two words under noise per clip, word times by Whisper); the App: SRS.orch one workflow for stand-alone and MAS, Speech Model Creation gives the offline network named by the segments, MMC Text To Speech speaks with it, both clients (table of damaged sections), the MAS Service serves the App's files; Fast tests pass; tried in the browser and the desktop client against a night copy of the Service. Left: the five networks (training), their measures and the table. Details in C:\\Users\\Thor\\Downloads\\night-2026-11-18.md.")

for n in ("N-28", "N-29", "N-30"):
    status(f"### {n} ", "blocked (2026-11-18 night: what is left is editing web pages, which the night job may not do; see the note)")
note_before_next("### N-28 ", "NOTE 2026-11-18 (night): BLOCKED. The rules of the night job say \"Do not edit any web page\" and \"Do not touch the user's Office files with automation\" (the export of the deck's slides through PowerPoint). Both are the whole of N-28. To be done by day, or the author lifts the two rules for N-28 (as for N-10).")
note_before_next("### N-29 ", "NOTE 2026-11-18 (night): BLOCKED. By day 2026-11-16 the schemas and the web pages of N-29/N-30 were done (see N-30); what is left is the AUF composite figure with Behaviour Planning (deck slide and web page): a web edit and a deck edit, which the night job may not do. Question: is anything else of N-29 still open for the software side? If not, mark it done by day once the figure is in.")
note_before_next("### N-30 ", "NOTE 2026-11-18 (night): BLOCKED, as N-29: only the AUF figure (web) is left, and the night job may not edit web pages.")

P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
