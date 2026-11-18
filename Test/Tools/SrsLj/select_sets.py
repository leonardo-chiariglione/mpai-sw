"""N-27: choose, once and with a fixed seed, the LJ Speech 1.1 clips for the five nested training sets
(1, 5, 15, 30, 60 minutes), a validation set and a held-out test set never used in any training.

python -I select_sets.py <LJSpeech-1.1 folder> <output folder>
Writes heldout.csv, validation.csv, train-01min.csv ... train-60min.csv (id|normalised text|seconds) and sets.json.
"""
import csv
import json
import random
import sys
import wave
from pathlib import Path

SEED = 20261115
MINUTES = [1, 5, 15, 30, 60]
HELDOUT = 20
VALIDATION = 10


def main() -> None:
    lj = Path(sys.argv[1])
    out = Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    rows = []
    with open(lj / "metadata.csv", encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("|")
            clip, text = parts[0], parts[2] if len(parts) > 2 and parts[2] else parts[1]
            with wave.open(str(lj / "wavs" / f"{clip}.wav")) as w:
                seconds = w.getnframes() / w.getframerate()
            rows.append((clip, text, round(seconds, 3)))
    rng = random.Random(SEED)
    order = list(range(len(rows)))
    rng.shuffle(order)
    # held-out test clips: between 6 and 10 s, so that two damaged sections fit in each
    heldout = [i for i in order if 6.0 <= rows[i][2] <= 10.0][:HELDOUT]
    rest = [i for i in order if i not in set(heldout)]
    validation = rest[:VALIDATION]
    pool = rest[VALIDATION:]

    def write(name, idx):
        with open(out / name, "w", encoding="utf-8", newline="\n") as f:
            for i in idx:
                f.write(f"{rows[i][0]}|{rows[i][1]}|{rows[i][2]}\n")

    write("heldout.csv", heldout)
    write("validation.csv", validation)
    sets = {}
    total, k, chosen = 0.0, 0, []
    for m in MINUTES:
        while total < m * 60:
            chosen.append(pool[k])
            total += rows[pool[k]][2]
            k += 1
        write(f"train-{m:02d}min.csv", chosen)
        sets[f"LJ-{m:02d}min"] = {"clips": len(chosen), "seconds": round(total, 1)}
    info = {
        "source": "LJ Speech 1.1 (keithito.com), public domain, one speaker",
        "seed": SEED,
        "clips_in_corpus": len(rows),
        "heldout": {"clips": len(heldout), "seconds": round(sum(rows[i][2] for i in heldout), 1),
                    "rule": "first 20 clips of 6-10 s in the shuffled order"},
        "validation": {"clips": len(validation), "seconds": round(sum(rows[i][2] for i in validation), 1)},
        "training_sets": sets,
        "nested": "each set is a prefix of the same shuffled order, so each contains the smaller ones",
    }
    (out / "sets.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
    print(json.dumps(info, indent=2))


if __name__ == "__main__":
    main()
