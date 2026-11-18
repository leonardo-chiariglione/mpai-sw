"""N-27: the voice configuration written by the training code (piper1-gpl) maps five diphthongs ("aɪ", "aʊ", "ɔɪ", "eɪ", "oʊ") to ids 161-165, which the piper.exe of this
machine refuses ("is not a single codepoint"). The training never used those ids (phoneme_check.py: the largest id used is 128): they are removed, and nothing changes for
the network. The original is kept as <name>.onnx.json.piper1.
python -I fix_configs.py <Models\\SRS-LJ>"""
import json
import shutil
import sys
from pathlib import Path

root = Path(sys.argv[1])
for cfg in sorted(root.glob("LJ-*/LJ-*.onnx.json")):
    data = json.loads(cfg.read_text(encoding="utf-8"))
    multi = [k for k in data.get("phoneme_id_map", {}) if len(k) > 1]
    if not multi:
        print(cfg.name, "nothing to remove")
        continue
    keep = cfg.with_name(cfg.name + ".piper1")
    if not keep.exists():
        shutil.copyfile(cfg, keep)
    for k in multi:
        del data["phoneme_id_map"][k]
    cfg.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(cfg.name, "removed", len(multi), "multi-character entries")
