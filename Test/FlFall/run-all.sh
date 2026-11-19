#!/bin/sh
# All methods, seeds 1..5 (or SEEDS="6 7 8"), the thesis's configuration (25 rounds x 100 local passes). About 35 min a seed on 12 processors.
cd "$(dirname "$0")"
dll=bin/Release/net10.0/Mpai.Aih.FlFall.dll
for s in ${SEEDS:-1 2 3 4 5}; do
  dotnet $dll central $s > /dev/null &
  for m in fedavg teacher addlearn local; do dotnet $dll $m $s > /dev/null; done
  wait
  echo "seed $s done $(date +%H:%M)"
done
