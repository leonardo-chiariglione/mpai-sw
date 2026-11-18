# SrsLj - the LJ Speech networks of the Speech Restoration System (N-27, night 2026-11-18)

Tools to make, set aside and test the five speech networks trained on 1, 5, 15, 30 and 60 minutes of LJ Speech.

| File | What |
|---|---|
| `select_sets.py` | chooses once (seed 20261115) the held-out, validation and nested training sets: `Test/Data/Speech/LJSpeech/*.csv`, `sets.json` |
| `copy_heldout.sh`, `copy_validation.sh` | copy those clips from WSL `~/srs/data/LJSpeech-1.1` into `Test/Data/Speech/LJSpeech` |
| `make_damaged.py` | the test material: each held-out clip at 16 kHz, two sections of two words covered by noise (word times by Whisper), `sections.json` |
| `train_fixed.py` | `python -m piper.train fit` with a FIXED split (training clips first, then the validation clips) and one best checkpoint kept |
| `train_one.sh` | one network: the run in WSL `~/srs/runs/LJ-xxmin`, then the ONNX voice, its config, the last checkpoint, the log and `times.txt` set aside in `Models/SRS-LJ/LJ-xxmin` |
| `train_all.sh` | the five, one after the other, the same budget |
| `export_base.sh` | the baseline LJ-00min: the base generator (kristin medium) not fine-tuned |
| `read_events.py`, `status.sh` | the steps, times and losses of a run (TensorBoard events) |
| `kill_pids.sh`, `stop_runs.sh` | stop these trainings only |
| `Measure/` | `SrsLjMeasure <out> <network> ...`: restores every held-out clip with each network THROUGH THE SRS APP (`SrsApp --network`), and measures speaker similarity (ECAPA) and the words heard (Whisper); `Test/Reports/srs-lj-test.json` |
| `make_app_files.py` | the ready-made recordings and the reader's speech offered by the App (`Apps/CAE/SRS/Files`) |
| `desktop_srs.ps1`, `desktop_dump.ps1` | the SRS App in the desktop client of the MAS App by UI Automation |
| `patch_*.py`, `todo_notes_*.py` | the edits made that night (kept to show what changed) |

The training code (OHF-Voice/piper1-gpl, GPL-3.0) and its Python environment are tools of this machine in WSL `~/srs/downloads`, not part of this software.
