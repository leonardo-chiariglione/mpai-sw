#!/bin/bash
# N-27: the baseline - the pretrained generator (kristin medium, not fine-tuned) exported as a voice like the five networks.
set -e
PY=$HOME/srs/downloads/piper1-gpl/.venv/bin/python
OUT=/mnt/d/AI/Models/SRS-LJ/LJ-00min
mkdir -p $OUT
CUDA_VISIBLE_DEVICES= $PY -m piper.train.export_onnx --checkpoint $HOME/srs/downloads/base-kristin/kristin-2000-clean.ckpt --output-file $OUT/LJ-00min.onnx
cp $HOME/srs/downloads/base-kristin/config.json $OUT/LJ-00min.onnx.json
cp $HOME/srs/downloads/base-kristin/MODEL_CARD $OUT/
echo "baseline: kristin medium (rhasspy/piper-checkpoints, epoch 2000), not fine-tuned; 0 minutes of LJ Speech" > $OUT/times.txt
ls -la $OUT
