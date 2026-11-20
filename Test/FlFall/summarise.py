# The runs of Test\FlFall (Test\Results\2026-11-19\fl-fall\runs.jsonl) summarised: mean and standard deviation over the seeds, per method, for the local and the global
# measures; written to summary.json and printed as a table.
import json, statistics as st, sys, os
path = sys.argv[1] if len(sys.argv) > 1 else r"D:\AI\Test\Results\2026-11-19\fl-fall\runs.jsonl"
runs = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
ORDER = ["local", "central", "fedavg", "addlearn", "teacher"]
KEYS = ["PrAuc", "RocAuc", "F1", "MissRate", "FalseAlarmRate"]
out = {}
for m in ORDER:
    rs = [r for r in runs if r["Method"] == m]
    if not rs: continue
    d = {"seeds": sorted(r["Seed"] for r in rs), "seconds": round(st.mean(r["Seconds"] for r in rs))}
    for side in ["Local", "Global"]:
        for k in KEYS:
            v = [r[side][k] for r in rs if r[side][k] is not None]
            d[f"{side} {k}"] = (round(st.mean(v), 4), round(st.stdev(v), 4) if len(v) > 1 else 0.0) if v else (None, None)   # no seed gave a value
    out[m] = d
json.dump(out, open(os.path.join(os.path.dirname(path), "summary.json"), "w"), indent=1)
print(f"{'method':9} {'n':>2}  " + "  ".join(f"{s[0]}.{k[:6]:>6}" for s in ['Local', 'Global'] for k in KEYS))
def cell(v): return "   no value  " if v[0] is None else f"{v[0]:.3f}±{v[1]:.3f}"
for m, d in out.items():
    print(f"{m:9} {len(d['seeds']):>2}  " + "  ".join(cell(d[f"{s} {k}"]) for s in ['Local', 'Global'] for k in KEYS))
