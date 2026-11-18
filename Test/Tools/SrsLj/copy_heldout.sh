#!/bin/bash
# N-27: copy the held-out LJ Speech clips (never trained on) into Test\Data\Speech\LJSpeech\heldout.
D=/mnt/d/AI/Test/Data/Speech/LJSpeech
mkdir -p $D/heldout
for c in $(cut -d'|' -f1 $D/heldout.csv); do cp ~/srs/data/LJSpeech-1.1/wavs/$c.wav $D/heldout/; done
ls $D/heldout | wc -l
