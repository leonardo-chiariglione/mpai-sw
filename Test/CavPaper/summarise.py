# N-35: the runs of the experiments (Test\Reports\cav-paper-runs-*.jsonl) summarised: Test\Reports\cav-odd-matrix.json, cav-paper-experiments.json,
# cav-paper-section.md (the tables), and the heat map of harm per cell (PNG) in Downloads.
# Run with the Python that has matplotlib: D:\AI\Support\Python\pyannote-venv\Scripts\python.exe -I summarise.py
import json, os, statistics as st, collections

REP = r"D:\AI\Test\Reports"
DOWN = r"C:\Users\Thor\Downloads"

def load(name):
    p = os.path.join(REP, name)
    if not os.path.exists(p): return []
    out = []
    for line in open(p, encoding="utf-8-sig"):
        line = line.strip()
        if line: out.append(json.loads(line))
    return out

CLASS = {"1": "car following", "2": "lane change and merge", "3": "lane change and merge", "4": "traffic jam", "5": "roadworks",
         "6": "junction", "7": "roundabout", "8": "vulnerable road users", "9": "vulnerable road users", "10": "hidden obstacle"}
CLASSES = ["car following", "lane change and merge", "traffic jam", "roadworks", "junction", "roundabout", "vulnerable road users", "hidden obstacle"]
def cls(r): return CLASS[r["Context"].split("-")[0]]
def sensing(r): return f"{'150 m' if r['Range'] == 150 else '60 m'}, {r['Noise']} m"
SENSING = ["150 m, 0.15 m", "150 m, 0.5 m", "60 m, 0.15 m", "60 m, 0.5 m"]
SURF = ["dry", "wet", "ice"]

def measures(rs):
    n = len(rs)
    if n == 0: return None
    ok = [r for r in rs if not r["Result"].startswith("error")]
    arrived = [r for r in ok if r["Result"] == "arrived"]
    return {
        "runs": n, "errors": n - len(ok),
        "collisions": sum(r["Collision"] for r in ok),
        "collisions with pedestrians or cyclists": sum(r["Collision"] and r["CollisionKind"] in ("Pedestrian", "Cyclist") for r in ok),
        "close calls": sum(r["CloseCall"] for r in ok),
        "hard brakings per run": round(sum(r["HardBrakings"] for r in ok) / max(1, len(ok)), 2),
        "stopped short": sum(r["StoppedShort"] for r in ok),
        "time / free-flow time (arrived, median)": round(st.median([r["Time"] / r["FreeFlowTime"] for r in arrived]), 2) if arrived else None,
        "max jerk m/s3 (median)": round(st.median([r["MaxJerk"] for r in ok]), 1) if ok else None,
        "seconds beyond grip": round(sum(r["GripExceeded"] for r in ok), 1),
        "harm (mean)": round(sum(r["Harm"] for r in ok) / max(1, len(ok)), 2),
    }

result = {"method": {}, "experiment1": {}, "experiment2": {}, "experiment3": {}}
lines = []
matrix = load("cav-paper-runs-matrix.jsonl")
failures = load("cav-paper-runs-failures.jsonl")

# ---------------- experiment 1 and 3: the ODD matrix, per motion implementation ----------------
if matrix:
    motions = [m for m in ["core", "idm", "planner"] if any(r["Motion"] == m for r in matrix)]
    odd = {}
    for m in motions:
        odd[m] = {}
        for c in CLASSES:
            for s in SURF:
                for z in SENSING:
                    rs = [r for r in matrix if r["Motion"] == m and cls(r) == c and r["Surface"] == s and sensing(r) == z]
                    if rs: odd[m][f"{c} | {s} | {z}"] = measures(rs)
        odd[m]["(all cells)"] = measures([r for r in matrix if r["Motion"] == m])
    result["experiment1"] = odd
    # per variant (the traffic of each context), core only, all surfaces and sensing
    result["experiment1_variants"] = {f"{k[0]} / {k[1]} / {k[2]}": measures(v) for k, v in
        sorted(collections.defaultdict(list, {}).items())}
    byvar = collections.defaultdict(list)
    for r in matrix: byvar[(r["Motion"], r["Context"], r["Variant"])].append(r)
    result["experiment1_variants"] = {f"{m} | {c} | {v}": measures(rs) for (m, c, v), rs in sorted(byvar.items())}
    json.dump({"motions": odd, "variants": result["experiment1_variants"]}, open(os.path.join(REP, "cav-odd-matrix.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)

    lines.append("### Experiment 1 - the ODD matrix (driving core)\n")
    for c in CLASSES:
        lines.append(f"\n**{c}** (runs per cell: {odd['core'].get(f'{c} | dry | 150 m, 0.15 m', {}).get('runs', 0)})\n")
        lines.append("| Surface | Sensing (range, noise) | Collisions | Close calls | Hard brakings / run | Stopped short | Time / free flow | Max jerk | Beyond grip (s) | Harm |")
        lines.append("|---|---|---|---|---|---|---|---|---|---|")
        for s in SURF:
            for z in SENSING:
                x = odd["core"].get(f"{c} | {s} | {z}")
                if x: lines.append(f"| {s} | {z} | {x['collisions']} | {x['close calls']} | {x['hard brakings per run']} | {x['stopped short']} | {x['time / free-flow time (arrived, median)']} | {x['max jerk m/s3 (median)']} | {x['seconds beyond grip']} | {x['harm (mean)']} |")

    lines.append("\n### Experiment 3 - interchangeable motion implementations, side by side (all cells of a road class together)\n")
    lines.append("| Road class | Implementation | Runs | Collisions | of them pedestrians/cyclists | Close calls | Hard brakings / run | Stopped short | Time / free flow | Harm |")
    lines.append("|---|---|---|---|---|---|---|---|---|---|")
    for c in CLASSES:
        for m in motions:
            x = measures([r for r in matrix if r["Motion"] == m and cls(r) == c])
            if x: lines.append(f"| {c} | {m} | {x['runs']} | {x['collisions']} | {x['collisions with pedestrians or cyclists']} | {x['close calls']} | {x['hard brakings per run']} | {x['stopped short']} | {x['time / free-flow time (arrived, median)']} | {x['harm (mean)']} |")
    lines.append("\n| Implementation | Runs | Errors | Collisions | Close calls | Stopped short | Harm (mean) |")
    lines.append("|---|---|---|---|---|---|---|")
    for m in motions:
        x = odd[m]["(all cells)"]
        lines.append(f"| {m} | {x['runs']} | {x['errors']} | {x['collisions']} | {x['close calls']} | {x['stopped short']} | {x['harm (mean)']} |")
    result["experiment3"] = {m: odd[m]["(all cells)"] for m in motions}

    # the heat map: harm per cell, one panel per implementation
    try:
        import matplotlib; matplotlib.use("Agg")
        import matplotlib.pyplot as plt, numpy as np
        from matplotlib.colors import LogNorm
        cols = [f"{s}\n{z}" for s in SURF for z in SENSING]
        fig, axes = plt.subplots(1, len(motions), figsize=(6.2 * len(motions), 5.2), constrained_layout=True)
        if len(motions) == 1: axes = [axes]
        vmax = max(x["harm (mean)"] for m in motions for k, x in odd[m].items() if k != "(all cells)") or 1
        for ax, m in zip(axes, motions):
            a = np.array([[odd[m].get(f"{c} | {s} | {z}", {}).get("harm (mean)", np.nan) for s in SURF for z in SENSING] for c in CLASSES], dtype=float)
            im = ax.imshow(np.maximum(a, 0.01), cmap="magma_r", norm=LogNorm(vmin=0.01, vmax=max(vmax, 0.02)), aspect="auto")
            ax.set_xticks(range(len(cols)), cols, fontsize=6.5, rotation=90); ax.set_yticks(range(len(CLASSES)), CLASSES, fontsize=8)
            ax.set_title({"core": "(a) driving core", "idm": "(b) IDM car-following", "planner": "(c) SpaceTime planner"}[m], fontsize=10)
            for i in range(a.shape[0]):
                for j in range(a.shape[1]):
                    if not np.isnan(a[i, j]): ax.text(j, i, f"{a[i, j]:.1f}", ha="center", va="center", fontsize=5.5, color="white" if a[i, j] > vmax / 8 else "black")
        fig.colorbar(im, ax=axes, shrink=0.8, label="harm per run (mean, log scale)")
        fig.suptitle("Harm per ODD cell: road class x surface x sensing (range, position noise)", fontsize=11)
        png = os.path.join(DOWN, "cav-odd-harm-heatmap.png"); fig.savefig(png, dpi=160); result["figure"] = png
    except Exception as e:
        result["figure"] = f"not drawn: {e}"

# ---------------- experiment 2: the failure layers ----------------
if failures:
    base = {(r["Context"], r["Variant"], r["Seed"]): r for r in failures if r["Injection"] == "none"}
    injs = [i for i in ["miss-p1", "miss-p5", "miss-p20", "miss-stretch0.5", "miss-stretch2", "phantom-pedestrian-frame", "phantom-pedestrian-1s",
                        "phantom-car-frame", "phantom-car-1s", "phantom-obstacle-frame", "phantom-obstacle-1s"] if any(r["Injection"] == i for r in failures)]
    exp2 = {}
    lines.append("\n### Experiment 2 - the failure layers (driving core, dry, 150 m, 0.15 m)\n")
    lines.append("| Injection | Road class | Runs | Collisions | New collisions (not in the run without injection) | Close calls | Needless hard brakings (more than without) | Stopped short |")
    lines.append("|---|---|---|---|---|---|---|---|")
    for inj in ["none"] + injs:
        for c in CLASSES + ["(all)"]:
            rs = [r for r in failures if r["Injection"] == inj and (c == "(all)" or cls(r) == c)]
            if not rs: continue
            newc = sum(1 for r in rs if r["Collision"] and not base.get((r["Context"], r["Variant"], r["Seed"]), {}).get("Collision", False))
            extra = sum(max(0, r["HardBrakings"] - base.get((r["Context"], r["Variant"], r["Seed"]), {}).get("HardBrakings", 0)) for r in rs)
            x = measures(rs)
            exp2[f"{inj} | {c}"] = dict(x, **{"new collisions": newc, "extra hard brakings": extra})
            if c == "(all)" or x["collisions"] or extra or newc:
                lines.append(f"| {inj} | {c} | {x['runs']} | {x['collisions']} | {newc} | {x['close calls']} | {extra} | {x['stopped short']} |")
    result["experiment2"] = exp2
    # the runs where an injection brought a collision: which ones
    result["experiment2_new_collisions"] = [f"{r['Injection']}: {r['Context']} {r['Variant']} seed {r['Seed']} ({r['CollisionKind']} at {r['CollisionSpeed']} m/s)"
        for r in failures if r["Injection"] != "none" and r["Collision"] and not base.get((r["Context"], r["Variant"], r["Seed"]), {}).get("Collision", False)]

result["method"] = {
    "simulation": "Test\\CavDriving (N-19/N-20), 0.1 s steps; Test\\CavPaper runner",
    "surfaces": "VRV friction: dry asphalt 0.9, wet 0.6, ice 0.15; the vehicle brakes and turns at most friction x 9.81 m/s2, never above 8 m/s2",
    "sensing": "range 150 or 60 m (as in fog), position noise 0.15 or 0.5 m, heading noise 0.03 rad",
    "harm": "pedestrian/cyclist collision 100 x (1 + impact speed m/s); vehicle/obstacle collision 20 x (1 + v); half if the CAV stood still; close call (TTC < 1 s or gap < 1 m) 5; hard braking (> 6 m/s2) 1 each; 10 per second beyond grip in a curve; stopped short 0.2 per second lost; late arrival 0.05 per second over 1.5 x free flow",
}
json.dump(result, open(os.path.join(REP, "cav-paper-experiments.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
open(os.path.join(REP, "cav-paper-section.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
print("figure:", result.get("figure"))
if failures: print("new collisions:", *result["experiment2_new_collisions"][:40], sep="\n  ")
