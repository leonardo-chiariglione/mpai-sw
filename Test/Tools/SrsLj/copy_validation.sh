#!/bin/bash
# N-27: the ten validation clips (not trained on, not in the test) - given to the SRS App as the speech of the speaker (Speech Segments for Modelling).
D=/mnt/d/AI/Test/Data/Speech/LJSpeech
mkdir -p $D/validation
for c in $(cut -d'|' -f1 $D/validation.csv); do cp ~/srs/data/LJSpeech-1.1/wavs/$c.wav $D/validation/; done
ls $D/validation | wc -l
