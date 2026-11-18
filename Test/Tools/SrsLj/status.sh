#!/bin/bash
# N-27: where the five trainings are.
cat ~/srs/runs/queue.txt
for n in 01 05 15 30 60; do
  f=$(ls ~/srs/runs/LJ-${n}min/run/lightning_logs/version_*/events.out.tfevents.* 2>/dev/null | tail -1)
  [ -n "$f" ] && { echo "LJ-${n}min:"; ~/srs/downloads/piper1-gpl/.venv/bin/python /mnt/d/AI/Test/Tools/SrsLj/read_events.py "$f" 2>&1 | grep -E '^(iters|val_mel|train_mel)'; }
done
