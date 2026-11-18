#!/bin/bash
# N-27: fine-tune the kristin base on one LJ set, then export the voice and set the network aside.
# bash train_one.sh <MM> <max_steps> <check_val_every_n_epoch>   (MM = 01, 05, 15, 30, 60)
set -e
MM=$1; STEPS=$2; VAL=$3
NAME=LJ-${MM}min
SETS=/mnt/d/AI/Test/Data/Speech/LJSpeech
RUN=$HOME/srs/runs/$NAME
OUT=/mnt/d/AI/Models/SRS-LJ/$NAME
PY=$HOME/srs/downloads/piper1-gpl/.venv/bin/python
mkdir -p "$RUN" "$OUT"
cd "$RUN"
cut -d'|' -f1,2 "$SETS/train-${MM}min.csv" > metadata.csv
cut -d'|' -f1,2 "$SETS/validation.csv" >> metadata.csv
export SRS_N_TRAIN=$(wc -l < "$SETS/train-${MM}min.csv")
START=$(date +%s)
echo "start $(date -Iseconds) steps=$STEPS val_every=$VAL n_train=$SRS_N_TRAIN" > times.txt
PYTHONPATH=/mnt/d/AI/Test/Tools/SrsLj $PY -m train_fixed fit \
  --data.voice_name "$NAME" --data.csv_path metadata.csv --data.audio_dir $HOME/srs/data/LJSpeech-1.1/wavs \
  --data.cache_dir cache --data.config_path config.json --data.espeak_voice en-us --data.batch_size ${BATCH:-8} \
  --model.sample_rate 22050 --model.warmstart_ckpt $HOME/srs/downloads/base-kristin/kristin-2000-clean.ckpt \
  --model.mos_metric none --model.lr_decay 1.0 --model.lr_decay_d 1.0 \
  --trainer.max_steps "$STEPS" --trainer.check_val_every_n_epoch "$VAL" --trainer.default_root_dir run \
  --trainer.enable_progress_bar false --trainer.log_every_n_steps 20 > train.log 2>&1
END=$(date +%s)
echo "end $(date -Iseconds) seconds=$((END-START))" >> times.txt
CK=$(ls -d run/lightning_logs/version_*/checkpoints | tail -1)
$PY -m piper.train.export_onnx --checkpoint "$CK/last.ckpt" --output-file "$OUT/$NAME.onnx" >> train.log 2>&1
cp config.json "$OUT/$NAME.onnx.json"
cp "$CK/last.ckpt" "$OUT/$NAME-last.ckpt"
cp times.txt metadata.csv train.log "$OUT/"
cp -r "$(dirname "$CK")"/events.out.tfevents.* "$(dirname "$CK")"/hparams.yaml "$(dirname "$CK")"/config.yaml "$OUT/" 2>/dev/null || true
echo "done $NAME" >> times.txt; cp times.txt "$OUT/"
