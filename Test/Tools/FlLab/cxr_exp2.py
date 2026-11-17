# Run: Support/Python/pyannote-venv/Scripts/python.exe -I cxr_exp2.py <Test/Data/ChestXray/features> <Test/Data/Fl/txv> <out.json> [--patients N] [--rounds R] [--tasks any,effusion]
#
# AIH-HSP, NIGHT REQUEST N-33, EXPERIMENT 2 (run by day 2026-11-17 at the author's request): THE LAST DENSENET BLOCK FINE-TUNED, ONE END USER PER PATIENT.
# NOT A CLINICAL TEST: the labels are mined by NLP from the radiology reports (noisy). A REFERENCE EXPERIMENT in PyTorch (the C# trainer of the AI Modules cannot
# train DenseNet layers); no watermark here (the NoConstraint AIMs are C#): it measures what fine-tuning the network itself brings.
#
# The network: TorchXRayVision DenseNet-121, CheXpert weights. Frozen up to transition3; TRAINED: denseblock4 and norm5 (2,160,128 parameters) and a head on the
# 1024 pooled features: standardisation (fixed: mean and spread of the Back End's own images under the CheXpert weights), Linear 1024->64, ReLU, Linear 64->1 (as the
# head of experiment 1). The input of denseblock4 of every image (512 x 7 x 7) was saved on 2026-11-15 (cxr-all-block4.bin, float16), so the first blocks never run.
# Batch normalisation keeps the running statistics of the CheXpert weights (eval mode): an End User has one to a few images, too few for batch statistics.
#
# The split, by patient, as experiment 1 (its own random draw, seed 22): the official test list is the check set; 10% of the other patients are the Back End's own
# (80% of them train its first model, 20% are kept for the quality gate); every other patient is one End User, whose last image is kept apart (held-out) when it
# has two or more; the threshold is fixed on the pooled held-out images (Youden's J).
#
# Steps: the Back End's first model (the head trained on its own images, the block as CheXpert left it); the CENTRAL model (all End Users' training images, 5 passes,
# block and head) to see what fine-tuning can bring; then FEDERATED, 5 rounds: every End User trains the current model on its own images (5 passes), the models are
# averaged weighted by their images, the gate keeps the average unless its AUC on the gate images falls by more than 0.01. Measured on the check set: AUC,
# accuracy, sensitivity, specificity at the threshold, 95% intervals (bootstrap over patients, 1000 draws); the AUC of 50 End Users' local models of round 1.
import argparse, copy, csv, json, os, sys, time
import numpy as np
import torch
import torch.nn as nn
import torchxrayvision as xrv
import multiprocessing as mp

C, H, W = 512, 7, 7
BLOCK_LR, HEAD_LR, PASSES, GATE_TOL = 1e-5, 1e-3, 5, 0.01


def load_index(folder):
    with open(os.path.join(folder, "cxr-all-index.csv"), newline="") as f:
        rows = list(csv.DictReader(f))
    return rows


class Tail(nn.Module):
    """denseblock4 + norm5 + ReLU + pooling + the fixed standardisation + the head."""
    def __init__(self, densenet, mean, spread):
        super().__init__()
        self.block, self.norm = copy.deepcopy(densenet.features.denseblock4), copy.deepcopy(densenet.features.norm5)   # each model its own copy
        self.register_buffer("mean", torch.tensor(mean, dtype=torch.float32))
        self.register_buffer("spread", torch.tensor(spread, dtype=torch.float32))
        g = torch.Generator().manual_seed(22)
        self.hidden, self.out = nn.Linear(1024, 64), nn.Linear(64, 1)
        for p in (self.hidden.weight, self.out.weight): nn.init.normal_(p, 0, 0.05, generator=g)
        nn.init.zeros_(self.hidden.bias); nn.init.zeros_(self.out.bias)

    def features(self, x):
        h = torch.relu(self.norm(self.block(x)))
        return torch.nn.functional.adaptive_avg_pool2d(h, 1).flatten(1)

    def forward(self, x):
        f = (self.features(x) - self.mean) / (self.spread + 1e-3)
        return self.out(torch.relu(self.hidden(f))).squeeze(1)


def trainable(model, block=True):
    head = list(model.hidden.parameters()) + list(model.out.parameters())
    if not block: return [{"params": head, "lr": HEAD_LR}]
    return [{"params": list(model.block.parameters()) + list(model.norm.parameters()), "lr": BLOCK_LR}, {"params": head, "lr": HEAD_LR}]


def train(model, x, y, passes, block=True, batch=32, seed=0):
    model.eval()                                   # batch normalisation keeps its running statistics
    for p in model.parameters(): p.requires_grad_(False)
    groups = trainable(model, block)
    for g in groups:
        for p in g["params"]: p.requires_grad_(True)
    opt = torch.optim.Adam(groups)
    loss_fn = nn.BCEWithLogitsLoss()
    n = len(y); r = np.random.default_rng(seed)
    for _ in range(passes):
        order = r.permutation(n)
        for at in range(0, n, batch):
            i = order[at:at + batch]
            opt.zero_grad()
            loss = loss_fn(model(x[i]), y[i]); loss.backward(); opt.step()
    for p in model.parameters(): p.requires_grad_(False)
    return model


def probabilities(model, x, batch=256):
    model.eval(); out = []
    with torch.no_grad():
        for at in range(0, len(x), batch): out.append(torch.sigmoid(model(x[at:at + batch])))
    return torch.cat(out).numpy().astype(np.float64)


def auc(p, y):
    order = np.argsort(p, kind="mergesort"); ranks = np.empty(len(p)); sp = p[order]
    i = 0
    while i < len(p):
        j = i
        while j + 1 < len(p) and sp[j + 1] == sp[i]: j += 1
        ranks[order[i:j + 1]] = (i + j) / 2 + 1; i = j + 1
    pos = y.sum(); neg = len(y) - pos
    if pos == 0 or neg == 0: return float("nan")
    return float((ranks[y == 1].sum() - pos * (pos + 1) / 2) / (pos * neg))


def measures(p, y, t):
    pos = p >= t
    tp = int(((pos) & (y == 1)).sum()); tn = int((~pos & (y == 0)).sum()); fp = int((pos & (y == 0)).sum()); fn = int((~pos & (y == 1)).sum())
    return auc(p, y), (tp + tn) / len(p), tp / (tp + fn) if tp + fn else float("nan"), tn / (tn + fp) if tn + fp else float("nan")


def youden(p, y):
    best, bj = 0.5, -9
    for t in np.unique(p):
        _, _, se, sp = measures(p, y, t)
        if se + sp - 1 > bj: bj, best = se + sp - 1, float(t)
    return best


def bootstrap(patients, p, y, t, draws):
    groups = {}
    for i, pt in enumerate(patients): groups.setdefault(pt, []).append(i)
    groups = list(groups.values()); r = np.random.default_rng(1); vals = [[], [], [], []]
    for _ in range(draws):
        idx = np.concatenate([groups[k] for k in r.integers(0, len(groups), len(groups))])
        for k, v in enumerate(measures(p[idx], y[idx], t)):
            if not np.isnan(v): vals[k].append(v)
    return [(float(np.quantile(v, 0.025)), float(np.quantile(v, 0.975))) if v else (float("nan"), float("nan")) for v in vals]


# ---- the workers of a federated round: each trains a share of the End Users and returns the sum of their parameters weighted by images
_W = {}


def _init(blocks_path, n_images, weights_dir, mean, spread, threads):
    torch.set_num_threads(threads)
    _W["blocks"] = np.memmap(blocks_path, dtype=np.float16, mode="r", shape=(n_images, C, H, W))
    dn = xrv.models.DenseNet(weights="densenet121-res224-chex", cache_dir=weights_dir)
    _W["model"] = Tail(dn, mean, spread)


def _work(args):
    state, users, ys, seed = args
    model = _W["model"]; blocks = _W["blocks"]
    total = {k: torch.zeros_like(v, dtype=torch.float64) for k, v in state.items()}; images = 0; aucs = []
    for k, (rows, keep_local) in enumerate(users):
        model.load_state_dict(state)
        x = torch.from_numpy(blocks[rows].astype(np.float32)); y = torch.tensor(ys[k], dtype=torch.float32)
        train(model, x, y, PASSES, block=True, batch=len(rows), seed=seed + k)
        sd = model.state_dict()
        for name in total: total[name] += sd[name].double() * len(rows)
        images += len(rows)
        if keep_local: aucs.append({n: t.clone() for n, t in sd.items()})
    return total, images, aucs


def main():
    a = argparse.ArgumentParser()
    a.add_argument("features"); a.add_argument("weights"); a.add_argument("out")
    a.add_argument("--patients", type=int, default=0); a.add_argument("--rounds", type=int, default=5)
    a.add_argument("--tasks", default="any,effusion"); a.add_argument("--draws", type=int, default=1000)
    a.add_argument("--locals", type=int, default=50); a.add_argument("--workers", type=int, default=4)
    args = a.parse_args()
    t0 = time.time(); say = lambda s: print(f"{s}  [{(time.time() - t0) / 60:.1f} min]", flush=True)
    rows = load_index(args.features); n_images = len(rows)
    blocks_path = os.path.join(args.features, "cxr-all-block4.bin")
    blocks = np.memmap(blocks_path, dtype=np.float16, mode="r", shape=(n_images, C, H, W))
    by_patient = {}
    for i, r in enumerate(rows): by_patient.setdefault(r["Patient"], []).append(i)
    check_p = sorted({r["Patient"] for r in rows if r["InTestList"] == "1"}, key=int)
    other_p = sorted({r["Patient"] for r in rows if r["InTestList"] != "1"}, key=int)
    if args.patients:
        rr = np.random.default_rng(5)
        check_p = list(rr.permutation(check_p)[:max(1, args.patients // 8)]); other_p = list(rr.permutation(other_p)[:args.patients - len(check_p)])
    rng = np.random.default_rng(22); other_p = list(rng.permutation(other_p))
    be_p = other_p[:max(1, len(other_p) // 10)]; eu_p = other_p[len(be_p):]
    gate_p = set(list(np.random.default_rng(23).permutation(be_p))[:max(1, len(be_p) // 5)])
    be_train = [i for p in be_p if p not in gate_p for i in by_patient[p]]; gate = [i for p in gate_p for i in by_patient[p]]
    check = [i for p in check_p for i in by_patient[p]]
    users, held = [], []
    for p in eu_p:
        imgs = sorted(by_patient[p], key=lambda i: rows[i]["Image"])
        if len(imgs) >= 2: users.append(imgs[:-1]); held.append(imgs[-1])
        else: users.append(imgs)
    dn = xrv.models.DenseNet(weights="densenet121-res224-chex", cache_dir=args.weights)
    torch.set_num_threads(os.cpu_count())
    base = Tail(dn, np.zeros(1024), np.ones(1024))

    def feats(idx):
        out = []
        with torch.no_grad():
            for at in range(0, len(idx), 256): out.append(base.features(torch.from_numpy(blocks[idx[at:at + 256]].astype(np.float32))))
        return torch.cat(out).numpy()
    fb = feats([i for p in be_p for i in by_patient[p]])
    mean, spread = fb.mean(0), fb.std(0)
    say(f"images {n_images}; check set {len(check)} of {len(check_p)} patients; Back End {len(be_train)} + gate {len(gate)}; End Users {len(users)} ({sum(map(len, users))} training images); held-out {len(held)}")
    report = {"Request": "N-33 experiment 2", "Made": time.strftime("%Y-%m-%d %H:%M"), "Tasks": []}
    for task in args.tasks.split(","):
        col = "Any" if task == "any" else "Effusion"
        lab = np.array([1 if r[col] == "1" else 0 for r in rows], dtype=np.int64)
        X = lambda idx: torch.from_numpy(blocks[idx].astype(np.float32))
        start = Tail(dn, mean, spread)
        train(start, X(be_train), torch.tensor(lab[be_train], dtype=torch.float32), 30, block=False, seed=1)   # the Back End's first model: the head on its own images
        say(f"== {task}: start model trained (head, 30 passes, on {len(be_train)} images)")
        xc, yc = X(check), lab[check]; xh, yh = X(held), lab[held]; xg, yg = X(gate), lab[gate]
        pts = [rows[i]["Patient"] for i in check]
        snapshots = [("start model (Back End)", start)]
        central = Tail(dn, mean, spread); central.load_state_dict(start.state_dict())
        all_tr = [i for u in users for i in u]
        train(central, X(all_tr), torch.tensor(lab[all_tr], dtype=torch.float32), PASSES, block=True, batch=32, seed=2)
        say(f"   central model trained ({len(all_tr)} images, {PASSES} passes, block and head)")
        current = start.state_dict(); local_aucs = []; rounds_out = []
        sampled = set(np.random.default_rng(77).permutation(len(users))[:args.locals].tolist())
        shares = [list(range(w, len(users), args.workers)) for w in range(args.workers)]
        with mp.get_context("spawn").Pool(args.workers, _init, (blocks_path, n_images, args.weights, mean, spread, max(1, os.cpu_count() // args.workers))) as pool:
            for rnd in range(1, args.rounds + 1):
                r0 = time.time()
                jobs = [({k: v.clone() for k, v in current.items()}, [(users[u], rnd == 1 and u in sampled) for u in share], [lab[users[u]] for u in share], 1000 * rnd + w) for w, share in enumerate(shares)]
                total, images, locals_ = None, 0, []
                for t, n, l in pool.imap_unordered(_work, jobs):
                    images += n; locals_ += l
                    if total is None: total = t
                    else:
                        for k in total: total[k] += t[k]
                avg = {k: (v / images).to(current[k].dtype) for k, v in total.items()}
                cand = Tail(dn, mean, spread); cand.load_state_dict(avg); cur = Tail(dn, mean, spread); cur.load_state_dict(current)
                a_new, a_old = auc(probabilities(cand, xg), yg), auc(probabilities(cur, xg), yg)
                kept = a_new >= a_old - GATE_TOL
                if kept: current = avg
                cur.load_state_dict(current); a_check = auc(probabilities(cur, xc), yc)
                for sd in locals_:
                    m = Tail(dn, mean, spread); m.load_state_dict(sd); local_aucs.append(auc(probabilities(m, xc), yc))
                rounds_out.append({"Round": rnd, "GateAUCBefore": round(a_old, 4), "GateAUCCandidate": round(a_new, 4), "Kept": kept, "CheckAUCOfCurrent": round(a_check, 4), "Seconds": round(time.time() - r0)})
                say(f"   round {rnd}: gate AUC {a_old:.3f} -> {a_new:.3f} {'kept' if kept else 'REFUSED'}; check-set AUC of the current model {a_check:.3f} ({time.time() - r0:.0f} s)")
                m = Tail(dn, mean, spread); m.load_state_dict(current); snapshots.append((f"federated after round {rnd}", m))
        snapshots.append(("central model (upper bound)", central))
        models = []
        for name, m in snapshots:
            ph, pc = probabilities(m, xh), probabilities(m, xc)
            t = youden(ph, yh); v = measures(pc, yc, t); ci = bootstrap(pts, pc, yc, t, args.draws)
            models.append({"Model": name, "Threshold": round(t, 4), "AUC": [round(v[0], 4), *map(lambda x: round(x, 4), ci[0])], "Accuracy": [round(v[1], 4), *map(lambda x: round(x, 4), ci[1])],
                           "Sensitivity": [round(v[2], 4), *map(lambda x: round(x, 4), ci[2])], "Specificity": [round(v[3], 4), *map(lambda x: round(x, 4), ci[3])], "HeldOutAUC": round(auc(ph, yh), 4)})
            say(f"   {name:<40} AUC {v[0]:.3f} [{ci[0][0]:.3f}-{ci[0][1]:.3f}]  acc {v[1]:.3f}  sens {v[2]:.3f}  spec {v[3]:.3f}  held-out AUC {auc(ph, yh):.3f}")
        la = sorted(x for x in local_aucs if not np.isnan(x))
        q = lambda f: round(float(np.quantile(la, f)), 4) if la else None
        say(f"   local models of round 1 ({len(la)}): AUC median {q(.5)}, quartiles {q(.25)}-{q(.75)}")
        report["Tasks"].append({"Task": task, "Groups": {"CheckImages": len(check), "CheckPatients": len(check_p), "BackEndImages": len(be_train), "GateImages": len(gate), "EndUsers": len(users),
                                 "EndUserTrainingImages": len(all_tr), "HeldOut": len(held)}, "Models": models, "Rounds": rounds_out,
                                "LocalModelsRound1": {"Sampled": len(la), "AUCMedian": q(.5), "AUCQ1": q(.25), "AUCQ3": q(.75)}, "Minutes": round((time.time() - t0) / 60, 1),
                                "Settings": {"Trained": "denseblock4 + norm5 (2,160,128 parameters) + head 1024-64-1; batch normalisation statistics frozen", "Rates": f"block {BLOCK_LR}, head {HEAD_LR}, Adam",
                                             "EndUser": f"{PASSES} passes, one batch", "Central": f"{PASSES} passes, batches of 32", "Gate": f"AUC on the gate images, kept unless it falls by more than {GATE_TOL}",
                                             "Watermark": "none (reference experiment in PyTorch)"}})
        with open(args.out, "w") as f: json.dump(report, f, indent=2)
    say("done")


if __name__ == "__main__":
    main()
