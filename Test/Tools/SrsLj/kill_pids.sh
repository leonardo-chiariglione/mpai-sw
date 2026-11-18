#!/bin/bash
# N-27: stop my own training processes, found by their exact program, never by a pattern that could match this script.
for p in $(ps -eo pid,args --no-headers | awk '$2 ~ /python$/ && $4 == "train_fixed" {print $1} $2 == "bash" && $3 ~ /SrsLj\/train_(one|all)\.sh$/ {print $1}'); do
  kill -9 $p && echo "killed $p"
done
