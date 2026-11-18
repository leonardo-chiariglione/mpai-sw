#!/bin/bash
# N-27: the five networks, one after the other, the SAME budget: 4,400 Lightning steps = 2,200 batches of 4 clips (the generator and the
# discriminator each count a step), constant learning rate, the same base. Validation (10 fixed clips) about every 220 batches.
export BATCH=4
STEPS=4400
cd $HOME/srs/runs
echo "queue start $(date -Iseconds)" > queue.txt
for spec in "01 73" "05 18" "15 7" "30 3" "60 2"; do
  set -- $spec
  rm -rf $HOME/srs/runs/LJ-$1min/run
  bash /mnt/d/AI/Test/Tools/SrsLj/train_one.sh $1 $STEPS $2 > LJ-$1min.out 2>&1
  echo "LJ-$1min exit $? $(date -Iseconds)" >> queue.txt
done
echo "queue end $(date -Iseconds)" >> queue.txt
