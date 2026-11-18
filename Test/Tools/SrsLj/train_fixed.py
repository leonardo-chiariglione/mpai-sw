"""N-27: `python -m piper.train fit ...` with three changes, so that the five LJ networks differ only in their minutes of speech.
1. The data split is FIXED, not random: the first SRS_N_TRAIN rows of the CSV are the training set, the rest the validation set
   (and the first two of them the audio examples). Piper's random split would take clips out of the 1-minute set.
2. The val_mos saver is dropped (the MOS predictor is off, see ~/srs/tools/train_wrapper.py); the val_mel saver keeps only the best one
   (each checkpoint is 846 MB); last.ckpt stays.
3. Nothing else: same base checkpoint, same steps, same batch size, constant learning rate (given on the command line).
"""
import os
import sys

from lightning.pytorch.callbacks import ModelCheckpoint
from torch.utils.data import Subset

import piper.train.__main__ as entry
from piper.train.vits.dataset import VitsDataModule

N_TRAIN = int(os.environ["SRS_N_TRAIN"])

_original_callbacks = entry._default_callbacks


def _callbacks():
    kept = []
    for c in _original_callbacks():
        if getattr(c, "monitor", None) == "val_mos":
            continue
        if isinstance(c, ModelCheckpoint) and c.monitor is not None:
            c.save_top_k = 1
        kept.append(c)
    return kept


entry._default_callbacks = _callbacks

_original_setup = VitsDataModule.setup


def _setup(self, stage):
    _original_setup(self, stage)
    full = self.train_dataset.dataset
    n = len(full)
    assert n > N_TRAIN, f"CSV has {n} usable rows, expected more than {N_TRAIN}"
    self.train_dataset = Subset(full, list(range(N_TRAIN)))
    self.val_dataset = Subset(full, list(range(N_TRAIN, n)))
    self.test_dataset = Subset(full, list(range(N_TRAIN, min(n, N_TRAIN + 2))))
    print(f"[N-27] fixed split: train {N_TRAIN}, validation {n - N_TRAIN}", flush=True)


VitsDataModule.setup = _setup
sys.argv[0] = "piper.train"
entry.main()
