"""N-27: the test material. For each held-out LJ clip: the original at 16 kHz mono, the times of its words (whisper.cpp, ggml-small, word level),
two sections of two words each covered by noise (one in the first half, one in the second), and the list of sections with their times and words
(from which the Damaged List CAE-DLS-V2.5 and the Text List are made).

python -I make_damaged.py <LJSpeech folder of this repository> <whisper-cli.exe> <ggml model>
Writes <folder>/original16k/<clip>.wav, <folder>/damaged/<clip>.wav, <folder>/sections.json.
"""
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

SEED = 20261115


def words_of(wav: Path, cli: str, model: str):
    with tempfile.TemporaryDirectory() as d:
        base = Path(d) / "w"
        subprocess.run([cli, "-m", model, "-f", str(wav), "-l", "en", "-ml", "1", "-sow", "-oj", "-of", str(base), "-np"],
                       check=True, capture_output=True)
        data = json.loads(Path(str(base) + ".json").read_text(encoding="utf-8"))
    out = []
    for seg in data["transcription"]:
        w = re.sub(r"[^a-z']", "", seg["text"].strip().lower())
        if w:
            out.append((w, seg["offsets"]["from"] / 1000.0, seg["offsets"]["to"] / 1000.0))
    return out


def pick(words, lo, hi):
    """two consecutive words, one of 4 letters or more, the pair starting between lo and hi of the words, lasting 0.4 s or more"""
    n = len(words)
    for i in range(int(n * lo), max(int(n * hi), int(n * lo) + 1)):
        if i + 1 >= n:
            break
        a, b = words[i], words[i + 1]
        if max(len(a[0]), len(b[0])) >= 4 and b[2] - a[1] >= 0.4:
            return i
    return None


def main() -> None:
    folder, cli, model = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    (folder / "original16k").mkdir(exist_ok=True)
    (folder / "damaged").mkdir(exist_ok=True)
    rng = np.random.default_rng(SEED)
    rows = [l.rstrip("\n").split("|") for l in open(folder / "heldout.csv", encoding="utf-8")]
    result = []
    for clip, text, _ in rows:
        audio, rate = sf.read(folder / "heldout" / f"{clip}.wav", dtype="float64")
        a16 = resample_poly(audio, 320, 441) if rate == 22050 else audio
        orig = folder / "original16k" / f"{clip}.wav"
        sf.write(orig, a16, 16000, subtype="PCM_16")
        words = words_of(orig, cli, model)
        i = pick(words, 0.15, 0.45)
        j = pick(words, 0.6, 0.9)
        if i is None or j is None or j <= i + 3:
            print(f"{clip}: no two sections found, skipped", flush=True)
            continue
        sections = []
        damaged = a16.copy()
        rms = float(np.sqrt(np.mean(a16 ** 2)))
        for k in (i, j):
            start, end = words[k][1], words[k + 1][2]
            s, e = int(round(start * 16000)), int(round(end * 16000))
            damaged[s:e] = rng.normal(0.0, rms, e - s)
            sections.append({"from": round(start, 3), "to": round(end, 3), "words": f"{words[k][0]} {words[k + 1][0]}"})
        sf.write(folder / "damaged" / f"{clip}.wav", np.clip(damaged, -1, 1), 16000, subtype="PCM_16")
        result.append({"clip": clip, "text": text, "seconds": round(len(a16) / 16000, 3), "sections": sections,
                       "whisper_words": " ".join(w for w, _, _ in words)})
        print(clip, sections, flush=True)
    (folder / "sections.json").write_text(json.dumps(result, indent=1), encoding="utf-8")
    print(len(result), "clips")


if __name__ == "__main__":
    main()
