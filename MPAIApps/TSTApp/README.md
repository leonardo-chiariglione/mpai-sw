# MPAI Text and Speech Translation - demo package

Reference implementation of MMC-TST-V2.5 on MPAI-AIF V3.0.

## What is in this folder

    TSTStandalone.exe     everything in one process
    TSTServer.exe         the MPAI-MAS server
    TSTClient.exe         the Remote Client Application
    TSTNetworked.bat      starts the server, waits, then the client
    *-config.json         one per executable

Double-click `TSTStandalone.exe` for the simple case, or `TSTNetworked.bat` for
the MPAI-MAS one. Do not start `TSTClient.exe` on its own: with no server it
falls back to running locally, which looks like success.

## What is NOT in this folder, and must be installed first

The executables are small; the MODELS are about 6 GB and are not distributed
here. Without them the application starts and fails at the first translation.

| Component | Size | Source |
|---|---|---|
| whisper.cpp binary and `ggml-small.bin` | 490 MB | github.com/ggerganov/whisper.cpp |
| Piper binary and one voice per language | 60 MB each | github.com/OHF-Voice/piper1-gpl, huggingface.co/rhasspy/piper-voices |
| M2M-100 418M, ONNX, encoder and two decoders | 940 MB quantised, 3.7 GB full precision | huggingface.co/Xenova/m2m100_418M |
| M2M-100 tokeniser: `sentencepiece.bpe.model`, `vocab.json`, `special_tokens_map.json` | 6 MB | huggingface.co/facebook/m2m100_418M |

Then edit `AIMs/aim-settings.json` so every path points at your copies. The
paths in the repository are absolute and belong to the machine this was built on;
this is the main obstacle to running it elsewhere and is known.

## Requirements

.NET 10 runtime. Windows for these executables; the same source builds and runs
on Linux, where the devices are `arecord` and `aplay` instead of WASAPI and
winmm.

## Documents

    TST-Architecture.md   the design, with diagrams, for someone who will not
                          read the code
    TST-Languages.md      how to add or change a language

## Licences

The code is MPAI Reference Software. The models are third party and each carries
its own terms: M2M-100 is MIT, Whisper is MIT, Piper voices are MIT, and the
Piper binary is GPL. NLLB-200 was deliberately NOT used because it is CC-BY-NC
and could not be redistributed with BSD-3-Clause reference software.
