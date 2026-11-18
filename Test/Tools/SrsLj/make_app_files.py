"""N-27: the files of the SRS App (Apps/CAE/SRS/Files), offered by the MAS Service with the App:
damaged/<clip>.wav (six of the held-out damaged clips) with damaged/<clip>.json (the sections: from, to, words - the client fills its table with them),
voice/LJ-reader.wav (one validation clip of the reader: the speech given as the Speech Segments for Modelling).
python -I make_app_files.py"""
import json
import shutil
from pathlib import Path

ROOT = Path(r"D:\AI")
DATA = ROOT / "Test" / "Data" / "Speech" / "LJSpeech"
OUT = ROOT / "Apps" / "CAE" / "SRS" / "Files"
CLIPS = ["LJ016-0277", "LJ031-0059", "LJ043-0017", "LJ015-0154", "LJ032-0216", "LJ010-0313"]

(OUT / "damaged").mkdir(parents=True, exist_ok=True)
(OUT / "voice").mkdir(parents=True, exist_ok=True)
sections = {c["clip"]: c for c in json.loads((DATA / "sections.json").read_text(encoding="utf-8"))}
for clip in CLIPS:
    shutil.copyfile(DATA / "damaged" / f"{clip}.wav", OUT / "damaged" / f"{clip}.wav")
    c = sections[clip]
    (OUT / "damaged" / f"{clip}.json").write_text(json.dumps({"clip": clip, "text": c["text"], "sections": c["sections"]}, indent=1), encoding="utf-8")
first_validation = sorted((DATA / "validation").glob("*.wav"))[0]
# the validation clips are 22,050 Hz LJ Speech files; the client brings speech to 16 kHz itself
shutil.copyfile(first_validation, OUT / "voice" / "LJ-reader.wav")
(OUT / "README.md").write_text(
    "# Files of the SRS App\n\n"
    "Offered by the MAS Service with the App (GET /Apps/SRS/Files/...), fetched by the client when the person picks one.\n\n"
    "- `damaged/<clip>.wav`: six clips of LJ Speech 1.1 (public domain, keithito.com) never used to train the networks, at 16 kHz, two sections of each covered by noise;"
    " `damaged/<clip>.json`: those sections (from, to in seconds, the words), with which the client fills the table of damaged sections.\n"
    f"- `voice/LJ-reader.wav`: {first_validation.stem}, a validation clip of the same reader (not trained on, not in the test), given as the Speech Segments for Modelling.\n\n"
    "Made by Test/Tools/SrsLj/make_app_files.py from Test/Data/Speech/LJSpeech (see its README).\n", encoding="utf-8")
print("ok", first_validation.name)
