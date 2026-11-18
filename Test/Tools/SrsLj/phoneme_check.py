"""N-27: which phoneme ids the training used (the cache of the 60-minute set), against the phoneme map of the config: multi-character entries and their ids."""
import glob
import json
import sys

import torch

run = sys.argv[1]
cfg = json.load(open(f"{run}/config.json", encoding="utf-8"))
pmap = cfg["phoneme_id_map"]
multi = {k: v for k, v in pmap.items() if len(k) > 1}
print("multi-character entries:", len(multi), list(multi.items())[:12])
multi_ids = {i for v in multi.values() for i in v}
used = set()
files = glob.glob(f"{run}/cache/*.phonemes.pt")
for f in files:
    used.update(torch.load(f).tolist())
print("files:", len(files), "ids used:", len(used), "max id:", max(used))
print("multi-character ids used in training:", sorted(used & multi_ids))
print("espeak:", cfg.get("espeak"), "phoneme_type:", cfg.get("phoneme_type"))
