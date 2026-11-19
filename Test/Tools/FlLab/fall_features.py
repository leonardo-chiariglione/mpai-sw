# THE WINDOWS OF UP-FALL, as in Leonor Ramos's MSc thesis (Iscte, 2026), chapter 3, variant "magnitude_features": the waist (belt) sensor only, accelerometer (g) and
# gyroscope (deg/s); low-pass 5 Hz; resampled to 20 Hz; windows of 1 s with 50 % overlap; channels acc x,y,z, gyro x,y,z plus the two magnitudes; for each channel the
# mean, standard deviation, minimum and maximum = 32 features. The thesis does not list its 32 features one by one: these are a reconstruction from its description.
# A window is a fall when the activity tag of its middle sample is 1-5 (the five kinds of fall); windows whose middle tag is 20 (unknown) are dropped.
# Run with the Python that has scipy: D:\AI\Support\Python\pyannote-venv\Scripts\python.exe -I fall_features.py <CompleteDataSet.csv> <out.csv>
import csv, sys, datetime
import numpy as np
from scipy.signal import butter, filtfilt

src, out = sys.argv[1], sys.argv[2]
FS = 20.0
rows = {}
with open(src, newline='') as f:
    r = csv.reader(f); h1 = next(r); next(r)
    ix = {n: i for i, n in enumerate(h1) if n}
    a0, g0 = ix['BeltAccelerometer'], ix['BeltAngularVelocity']
    for row in r:
        try:
            t = datetime.datetime.fromisoformat(row[0]).timestamp()
            v = [float(row[a0 + k]) for k in range(3)] + [float(row[g0 + k]) for k in range(3)]
        except ValueError:
            continue
        key = (int(row[ix['Subject']]), int(row[ix['Activity']]), int(row[ix['Trial']]))
        rows.setdefault(key, []).append((t, v, int(row[ix['Tag']])))

b, a = butter(4, 5.0 / (FS / 2), 'low')
n_out = 0
with open(out, 'w', newline='') as f:
    w = csv.writer(f)
    w.writerow(['subject', 'activity', 'trial', 'fall'] + [f'f{i}' for i in range(32)])
    for (s, act, tr), samples in sorted(rows.items()):
        samples.sort(key=lambda x: x[0])
        t = np.array([x[0] for x in samples]); t -= t[0]
        x = np.array([x[1] for x in samples]); tag = np.array([x[2] for x in samples])
        keep = np.concatenate([[True], np.diff(t) > 0]); t, x, tag = t[keep], x[keep], tag[keep]
        if t[-1] < 2: continue
        grid = np.arange(0, t[-1], 1 / FS)
        xr = np.stack([np.interp(grid, t, x[:, k]) for k in range(6)], 1)       # resampled to 20 Hz (the source is about 18-20 Hz)
        if len(grid) > 27: xr = filtfilt(b, a, xr, axis=0)                      # low-pass 5 Hz
        tr_tag = tag[np.clip(np.searchsorted(t, grid), 0, len(tag) - 1)]
        ch = np.concatenate([xr, np.linalg.norm(xr[:, :3], axis=1, keepdims=True), np.linalg.norm(xr[:, 3:], axis=1, keepdims=True)], 1)
        win = int(FS)
        for st in range(0, len(grid) - win + 1, win // 2):
            seg = ch[st:st + win]; mid = tr_tag[st + win // 2]
            if mid == 20: continue
            feats = np.concatenate([seg.mean(0), seg.std(0), seg.min(0), seg.max(0)])
            w.writerow([s, act, tr, int(1 <= mid <= 5)] + [f'{v:.6g}' for v in feats]); n_out += 1
print('windows', n_out, 'trials', len(rows))
