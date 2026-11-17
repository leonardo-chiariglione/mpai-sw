# Run: Support/Python/pyannote-venv/Scripts/python.exe -I cxr_features_more.py <Test/Data/ChestXray> <Test/Data/Fl/txv>
#
# AIH-HSP, night request N-33 (2026-11-15), preparation. The images of images_001 to images_004 (NIH ChestX-ray14), for the federated learning with ONE END USER
# PER PATIENT (experiment 1) and the last DenseNet block fine-tuned (experiment 2):
#  1. unpacks images_002, images_003 and images_004 into <ChestXray>/images, where they are not there yet (images_001 is there since N-22);
#  2. for EVERY image, with the network and the preprocessing of N-22 (TorchXRayVision DenseNet-121, weights "densenet121-res224-chex", CheXpert only; grey,
#     xrv normalize, centre crop, resize 224), in ONE forward pass:
#       - the 1024 features (as features2 gives them: relu, global average pool) -> features/cxr-all-features.bin (n x 1024 float32, little endian);
#       - the input of the last dense block (the output of transition3: 512 x 7 x 7) -> features/cxr-all-block4.bin (n x 25088 float16, little endian),
#         from which experiment 2 trains denseblock4 + norm5 + the head without running the first blocks again;
#     and features/cxr-all-index.csv (name, patient, age, sex, view, any finding, effusion, in the official test list), in the same order.
#     The first batch is checked: the features of the pass must be those of model.features2.
#  The N-22 files (cxr-features.bin, cxr-index.csv, cxr-head.onnx) are left as they are.
import csv, os, sys, tarfile, time
import numpy as np
import torch
import torch.nn.functional as F
import skimage.io
import torchxrayvision as xrv

data, weights_dir = sys.argv[1], sys.argv[2]
images = os.path.join(data, "images")
out = os.path.join(data, "features")
os.makedirs(out, exist_ok=True)

for k in (2, 3, 4):
    tar_path = os.path.join(data, f"images_{k:03d}.tar.gz")
    marker = os.path.join(out, f"unpacked-{k:03d}.done")
    if os.path.exists(marker):
        continue
    t = time.time()
    with tarfile.open(tar_path, "r:gz") as tar:
        tar.extractall(data, filter="data")
    open(marker, "w").close()
    print(f"images_{k:03d} unpacked in {time.time() - t:.0f} s", flush=True)

names = sorted(n for n in os.listdir(images) if n.endswith(".png"))
print("images", len(names), flush=True)
rows = {}
with open(os.path.join(data, "Data_Entry_2017_v2020.csv"), newline="") as f:
    for r in csv.DictReader(f):
        rows[r["Image Index"]] = r
with open(os.path.join(data, "test_list.txt")) as f:
    test = set(l.strip() for l in f if l.strip())

torch.set_num_threads(os.cpu_count() or 4)
model = xrv.models.DenseNet(weights="densenet121-res224-chex", cache_dir=weights_dir).eval()
crop, resize = xrv.datasets.XRayCenterCrop(), xrv.datasets.XRayResizer(224, engine="skimage")


def prepare(path):
    img = skimage.io.imread(path)
    if img.ndim == 3:
        img = img[:, :, :3].mean(2)
    img = xrv.datasets.normalize(img.astype(np.float32), 255)
    img = resize(crop(img[None, ...]))
    return img.astype(np.float32)


def both(x):
    """The features and the input of denseblock4, in one pass."""
    h, block4 = x, None
    for name, layer in model.features.named_children():
        h = layer(h)
        if name == "transition3":
            block4 = h
    f = F.adaptive_avg_pool2d(F.relu(h), (1, 1)).flatten(1)
    return f, block4


feats_path, block_path = os.path.join(out, "cxr-all-features.bin"), os.path.join(out, "cxr-all-block4.bin")
batch, t = 32, time.time()
with torch.no_grad(), open(feats_path, "wb") as ff, open(block_path, "wb") as fb:
    for at in range(0, len(names), batch):
        x = torch.from_numpy(np.stack([prepare(os.path.join(images, n)) for n in names[at:at + batch]]))
        f, b = both(x)
        if at == 0:
            ref = model.features2(x)
            err = float((ref - f).abs().max())
            print(f"check against features2: max difference {err:.2e}; block-4 input {tuple(b.shape[1:])}", flush=True)
            assert err < 1e-4, "the features of the pass are not those of features2"
        f.numpy().astype("<f4").tofile(ff)
        b.numpy().astype("<f2").reshape(len(x), -1).tofile(fb)
        if at % 3200 == 0:
            print(f"{at + len(x)} / {len(names)}  {time.time() - t:.0f} s", flush=True)

with open(os.path.join(out, "cxr-all-index.csv"), "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["Image", "Patient", "Age", "Sex", "View", "Any", "Effusion", "InTestList"])
    for n in names:
        r = rows[n]
        labels = r["Finding Labels"].split("|")
        w.writerow([n, r["Patient ID"], r["Patient Age"], r["Patient Sex"], r["View Position"],
                    0 if labels == ["No Finding"] else 1, 1 if "Effusion" in labels else 0, 1 if n in test else 0])
print(f"done: {len(names)} images in {time.time() - t:.0f} s", flush=True)
