#!/bin/sh
# All methods, seeds 1..5 (or SEEDS="6 7 8"), the thesis's configuration (25 rounds x 100 local passes). About 9 min a seed on UP-Fall on 12 processors.
# Another dataset: CSV=D:/AI/Test/Data/Fall/erciyes-windows.csv OUT=D:/AI/Test/Results/<date>/fl-fall-erciyes/runs.jsonl (default: UP-Fall, the runs of 2026-11-19).
cd "$(dirname "$0")"
dll=bin/Release/net10.0/Mpai.Aih.FlFall.dll
csv=${CSV:-D:/AI/Test/Data/Fall/upfall-windows.csv}
out=${OUT:-D:/AI/Test/Results/2026-11-19/fl-fall/runs.jsonl}
for s in ${SEEDS:-1 2 3 4 5}; do
  dotnet $dll central $s --csv "$csv" --out "$out" > /dev/null &
  for m in fedavg teacher addlearn local; do dotnet $dll $m $s --csv "$csv" --out "$out" > /dev/null; done
  wait
  echo "seed $s done $(date +%H:%M)"
done
