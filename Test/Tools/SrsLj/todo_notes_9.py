"""Night 2026-11-18: N-27 done."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
head = "### N-27 "
i = s.index(head); j = s.index("Status: ", i); k = s.index("\n", j)
s = s[:j] + "Status: done (2026-11-18 night: five networks set aside, measured, the App in the MAS App; see the note)" + s[k:]
i = s.index(head); j = s.index("\n### ", i + len(head))
s = s[:j].rstrip("\n") + "\n" + """NOTE 2026-11-18 08:10 (night): DONE. Nothing committed.
- Networks (Models\\SRS-LJ\\LJ-01min ... LJ-60min, each: <name>.onnx + .onnx.json, the last checkpoint, train.log, the TensorBoard events, times.txt; training-summary.json): the same base (kristin-2000), the SAME budget for the five - 2,200 batches of 4 clips (Lightning max_steps 4,400), constant learning rate, a fixed split (the set, then 10 validation clips). Times: 58, 81, 85, 87, 82 min (longer clips cost more a batch). Batch size 4, not 8: at 8 the 4 GB of the GTX 1050 Ti overflowed and a batch took 10 s. LJ-00min = the base not fine-tuned (the baseline). A first LJ-01min of 1,100 batches is kept aside in Models\\SRS-LJ-extra.
- The networks' config from the training code maps five diphthongs to ids 161-165, which the piper.exe of this machine refuses; the training never used them (largest id 128): removed (fix_configs.py; originals kept as .onnx.json.piper1).
- Test: 20 held-out clips (Test\\Data\\Speech\\LJSpeech, README), two sections of two words covered by noise in each, restored THROUGH THE SRS APP by each network; ECAPA similarity of the restored sections to the reader's real voice (10 validation clips) and Whisper on the result. Table in C:\\Users\\Thor\\Downloads\\night-2026-11-18.md; WAVs in C:\\Users\\Thor\\Downloads\\SRS-LJ-test (originals, damaged, one folder per network). In short: any training on the reader (even 1 minute) moves the voice from 0.18 to about 0.62 similarity; 1 to 60 minutes do not differ at this budget (0.60-0.63); the words heard stay 68-73 of 80.
- The App: SRS.orch one workflow (stand-alone and MAS): avatar explanation, six ready-made damaged recordings or a WAV, the table of damaged sections (filled in for a ready-made one), the network chosen (LJ-01min ... LJ-60min, LJ-00min), Play again. Speech Model Creation gives the network trained offline named by the segments (MLModelData DataID); MMC Text To Speech speaks with the network of that ID from its folder (AIM-5: no AIM opens a file named in data). MAS Service: GET /Apps/{id}/Files/... (the App's files); codec CAE-DLS-V2.5. Browser and desktop clients (table, ready-made speech keeps its name). Tried in a night copy of the MAS Service: browser (LJ-00min at 01:10, LJ-60min at 08:05) and desktop (UI Automation): the whole flow to Play again; screenshots srs-app-*.jpg in Downloads.
- Tests: MasSrsAppTests (5, Fast) + SrsAppNetworkTests (2, Slow); CAE-USC App/Module/Voice/AES-SME tests and AIM-5 conformance pass.
- Choices I made: batch 4 and 2,200 batches (time: about 7 h for the five); validation = 10 fixed clips; the network found by the ID of the Speech Segments for Modelling (BasicSpeechObjectID = the network's name); test sections at word boundaries from Whisper's word times; 'real voice at the sections' measured on cut fragments (lower than whole synthesised words).""" + "\n" + s[j:]
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
