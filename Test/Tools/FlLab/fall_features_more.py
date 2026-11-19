# THE WINDOWS OF TWO MORE OPEN FALL DATASETS, made the same way as fall_features.py makes those of UP-Fall (Leonor Ramos's thesis, "magnitude_features"): the WAIST
# sensor only, accelerometer (g) and gyroscope (deg/s); low-pass 5 Hz; 20 Hz; windows of 1 s with 50 % overlap; acc x,y,z, gyro x,y,z and the two magnitudes; mean,
# standard deviation, minimum and maximum of each = 32 features. Output: the same CSV layout as upfall-windows.csv (subject, activity, trial, fall, f0..f31).
#   erciyes  "Simulated Falls and Daily Living Activities" (Ozdemir and Barshan, Sensors 2014; UCI 455, CC BY 4.0): Xsens MTw, waist = sensor 340535, 25 Hz,
#            Acc in m/s2, Gyr in rad/s; 17 volunteers (folders 1xx, 2xx), activities 801-816 (daily living) and 901-920 (falls), Test_1..Test_5 or 6.
#   umafall  UMAFall (Casilari et al., Universidad de Malaga; figshare 4214283, corrected version, CC BY 4.0): SensorTag on the WAIST (its Sensor_ID read from each
#            file's header), about 20 Hz, acceleration in g, gyroscope in deg/s; 19 subjects, the activity and the trial in the file name.
# LABELS. Neither dataset marks the fall sample by sample (UP-Fall does): a recording is a fall or not. In a FALL recording the windows whose middle lies within 1 s of
# the peak of the acceleration magnitude are falls, the others of that recording are dropped (they are standing before or lying after, neither a fall nor a clean
# daily activity). Every window of a daily-living recording is a non-fall.
# HELD-OUT DATA. FlFall (Test\FlFall) keeps trial 3 of each End User as its own held-out data. Here the LAST trial of every (subject, activity) that has at least
# two trials is written as trial 3, the others as trial 1, so that Program.cs is used unchanged.
# Run with the Python that has scipy: D:\AI\Support\Python\pyannote-venv\Scripts\python.exe -I fall_features_more.py erciyes|umafall <data folder> <out.csv>
import csv, os, re, sys
import numpy as np
from scipy.signal import butter, filtfilt

kind, root, out = sys.argv[1], sys.argv[2], sys.argv[3]
FS = 20.0
b, a = butter(4, 5.0 / (FS / 2), 'low')


def erciyes():
    for subj in sorted(os.listdir(root)):
        base = os.path.join(root, subj, 'Testler Export')
        if not os.path.isdir(base): continue
        for act_dir in sorted(os.listdir(base)):
            act = int(act_dir[:3])
            for test in sorted(os.listdir(os.path.join(base, act_dir))):
                f = os.path.join(base, act_dir, test, '340535.txt')
                if not re.fullmatch(r'Test_\d+', test) or not os.path.isfile(f): continue      # folders such as Test_3Fail are tests the authors marked as failed
                rows = []
                with open(f, encoding='latin-1') as h:
                    head = None
                    for line in h:
                        if line.startswith('//'): continue
                        p = line.rstrip('\n').rstrip('\t').split('\t')
                        if head is None: head = {n: i for i, n in enumerate(p)}; continue
                        try:
                            acc = [float(p[head[c]]) / 9.80665 for c in ('Acc_X', 'Acc_Y', 'Acc_Z')]
                            gyr = [float(p[head[c]]) * 180 / np.pi for c in ('Gyr_X', 'Gyr_Y', 'Gyr_Z')]
                        except (ValueError, IndexError, KeyError):
                            continue
                        rows.append(acc + gyr)
                if len(rows) < 50: continue
                t = np.arange(len(rows)) / 25.0
                yield int(subj), act, int(test.split('_')[1]), act >= 900, t, np.array(rows)


def umafall():
    pat = re.compile(r'UMAFall_Subject_(\d+)_(ADL|Fall)_(.+)_(\d+)_\d{4}-\d\d-\d\d')
    names = {}
    for fn in sorted(os.listdir(root)):
        m = pat.match(fn)
        if not m: continue
        s, typ, act, tr = int(m[1]), m[2], m[3], int(m[4])
        names.setdefault(act, len(names) + 1)
        waist = None; acc = {}; gyr = {}
        with open(os.path.join(root, fn), encoding='latin-1') as h:
            for line in h:
                if line.startswith('%'):
                    q = [x.strip() for x in line[1:].split(';')]
                    if len(q) >= 3 and q[2] == 'WAIST': waist = q[1]
                    continue
                q = line.strip().split(';')
                if len(q) < 7 or waist is None or q[6] != waist: continue
                try:
                    ts, v = float(q[0]) / 1000.0, [float(q[2]), float(q[3]), float(q[4])]
                except ValueError:
                    continue
                (acc if q[5] == '0' else gyr if q[5] == '1' else {})[ts] = v
        if len(acc) < 50 or len(gyr) < 50: continue
        ta = np.array(sorted(acc)); tg = np.array(sorted(gyr))
        A = np.array([acc[k] for k in ta]); G = np.array([gyr[k] for k in tg])
        G = np.stack([np.interp(ta, tg, G[:, k]) for k in range(3)], 1)              # the gyroscope on the accelerometer's times
        yield s, 100 * (typ == 'Fall') + names[act], tr, typ == 'Fall', ta - ta[0], np.concatenate([A, G], 1)


recs = list(erciyes() if kind == 'erciyes' else umafall())
last = {}
for s, act, tr, *_ in recs: last.setdefault((s, act), []).append(tr)
n_out = n_fall = 0
with open(out, 'w', newline='') as f:
    w = csv.writer(f)
    w.writerow(['subject', 'activity', 'trial', 'fall'] + [f'f{i}' for i in range(32)])
    for s, act, tr, is_fall, t, x in recs:
        trs = last[(s, act)]
        trial = 3 if len(trs) >= 2 and tr == max(trs) else 1
        keep = np.concatenate([[True], np.diff(t) > 0]); t, x = t[keep], x[keep]
        if t[-1] < 2: continue
        grid = np.arange(0, t[-1], 1 / FS)
        xr = np.stack([np.interp(grid, t, x[:, k]) for k in range(6)], 1)
        if len(grid) > 27: xr = filtfilt(b, a, xr, axis=0)
        ch = np.concatenate([xr, np.linalg.norm(xr[:, :3], axis=1, keepdims=True), np.linalg.norm(xr[:, 3:], axis=1, keepdims=True)], 1)
        peak = grid[int(np.argmax(ch[:, 6]))]
        win = int(FS)
        for st in range(0, len(grid) - win + 1, win // 2):
            mid = grid[st + win // 2]
            if is_fall and abs(mid - peak) > 1.0: continue
            seg = ch[st:st + win]
            feats = np.concatenate([seg.mean(0), seg.std(0), seg.min(0), seg.max(0)])
            w.writerow([s, act, trial, int(is_fall)] + [f'{v:.6g}' for v in feats]); n_out += 1; n_fall += int(is_fall)
print(kind, 'recordings', len(recs), 'subjects', len({r[0] for r in recs}), 'windows', n_out, 'falls', n_fall)
