"""N-27: what each network's training did: clips, seconds of training, batches, first and last validation mel loss, last training mel loss.
Reads Models/SRS-LJ/<name>/times.txt and its TensorBoard events. python summarise_training.py <Models/SRS-LJ>  (in the training venv: tensorboard)"""
import glob
import json
import re
import sys
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

rows = []
for folder in sorted(Path(sys.argv[1]).glob("LJ-*")):
    times = (folder / "times.txt").read_text() if (folder / "times.txt").exists() else ""
    m = re.search(r"seconds=(\d+)", times)
    n = re.search(r"n_train=(\d+)", times)
    ev = glob.glob(str(folder / "events.out.tfevents.*"))
    row = {"network": folder.name, "clips": int(n.group(1)) if n else None, "seconds": int(m.group(1)) if m else None}
    if ev:
        acc = EventAccumulator(ev[0], size_guidance={"scalars": 0}); acc.Reload()
        tags = acc.Tags()["scalars"]
        if "iters" in tags: row["batches"] = int(acc.Scalars("iters")[-1].value) + 1
        if "val_mel" in tags: row["val_mel_first"], row["val_mel_last"] = round(acc.Scalars("val_mel")[0].value, 4), round(acc.Scalars("val_mel")[-1].value, 4)
        if "train_mel" in tags: row["train_mel_last"] = round(acc.Scalars("train_mel")[-1].value, 4)
    rows.append(row)
out = Path(sys.argv[1]) / "training-summary.json"
out.write_text(json.dumps(rows, indent=1))
print(json.dumps(rows, indent=1))
