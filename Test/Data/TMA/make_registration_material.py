"""IS1009c: the reference who-speaks-when for 10:00-15:00 (times relative to the clip) and, for each speaker, about 12 s of speech from his or her
own headset taken BEFORE 10:00 (segments where nobody else speaks), for registration with Access Control Registration."""
import re, wave, struct, sys, os, csv

ROOT = r"D:\AI\Data\AMI"
SEG = os.path.join(ROOT, "annotations", "segments")
OUT = os.path.join(ROOT, "IS1009c", "enrol")
os.makedirs(OUT, exist_ok=True)
CLIP_START, CLIP_END = 600.0, 900.0
CHANNEL = {"A": 0, "B": 1, "C": 2, "D": 3}

segs = {}
for spk in CHANNEL:
    x = open(os.path.join(SEG, f"IS1009c.{spk}.segments.xml"), encoding="utf-8").read()
    segs[spk] = sorted((float(a), float(b)) for a, b in re.findall(r'transcriber_start="([\d.]+)"\s+transcriber_end="([\d.]+)"', x))

# the reference, relative to the clip
with open(os.path.join(ROOT, "IS1009c", "IS1009c-10m00-15m00-who-speaks-when.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["speaker", "start_s", "end_s"])
    rows = []
    for spk, ss in segs.items():
        for a, b in ss:
            if b > CLIP_START and a < CLIP_END:
                rows.append((spk, round(max(a, CLIP_START) - CLIP_START, 2), round(min(b, CLIP_END) - CLIP_START, 2)))
    for r in sorted(rows, key=lambda r: r[1]): w.writerow(r)
print("reference segments in the clip:", len(rows), {s: round(sum(e - b for k, b, e in rows if k == s), 1) for s in CHANNEL})

def overlaps(spk, a, b):
    return sum(max(0.0, min(e, b) - max(s, a)) for o, ss in segs.items() if o != spk for s, e in ss) > 0.5

for spk, ch in CHANNEL.items():
    src = wave.open(os.path.join(ROOT, "IS1009c", f"IS1009c.Headset-{ch}.wav"))
    rate, width, nch = src.getframerate(), src.getsampwidth(), src.getnchannels()
    picked, total = [], 0.0
    for a, b in segs[spk]:
        if not (b < CLIP_START - 5 or a > CLIP_END + 5): continue   # outside the clip, with a margin
        if b - a < 1.5 or overlaps(spk, a, b): continue    # clean, not too short
        picked.append((a, b)); total += b - a
        if total >= 12: break
    data = b""
    for a, b in picked:
        src.setpos(int(a * rate)); data += src.readframes(int((b - a) * rate))
    out = wave.open(os.path.join(OUT, f"{spk}.wav"), "wb"); out.setnchannels(nch); out.setsampwidth(width); out.setframerate(rate); out.writeframes(data); out.close()
    print(spk, f"channel {ch}", f"{total:.1f} s from {len(picked)} segments", f"rate {rate} width {width} ch {nch}", [f"{a:.1f}-{b:.1f}" for a, b in picked][:6])
