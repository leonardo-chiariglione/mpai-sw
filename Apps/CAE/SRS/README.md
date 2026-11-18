# Speech Restoration System

Repairs a recording of speech where some words are lost under noise: the lost words are said again in the voice of the speaker and put in their place. The voice is a Speech Model made from speech of the speaker: a **network trained offline** on 1, 5, 15, 30 or 60 minutes of the LJ Speech reader (Piper/VITS fine-tuned, `Models\SRS-LJ`), or, in the stand-alone App, the OpenVoice V2 tone colour of any speaker.

| Part | What |
|---|---|
| The App | `SrsApp` (`App\`) stand-alone; in the MAS App, the App "Speech Restoration System" of the MPAI-MAS Service |
| Workflow | `SRS.orch`: the SAME workflow stand-alone and in the MAS App |
| User Agent | `AIF/UserAgent/Ua` (`Mpai.Ua`): reads `SRS.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM and no use case |
| Controller | the Controller of the Module (stand-alone: in the App's process, `Apps/CAE/Support`, `ModuleHost`; in MAS: the Service's) |
| Module | `1CAE-SRS-V2.5-I01`: Speech Model Creation, Text To Speech (MPAI-MMC), Speech Restoration Assembly |
| Files | `Files\`: six ready-made damaged recordings of the reader (never trained on) with their sections, and speech of the reader; served by the Service at `Apps/SRS/Files/...` |

## What the person does (MAS App, browser or desktop client)

1. The avatar says what the App does, and that the network is of ONE speaker, the LJ Speech reader.
2. Choose a damaged recording (LJ016-0277, LJ031-0059, ...) or a WAV file of the same speaker.
3. The table of damaged sections opens - from (s), to (s), the words said there - filled in for a ready-made recording. OK gives the Damaged List and the Text List.
4. Choose the network: LJ-01min, LJ-05min, LJ-15min, LJ-30min, LJ-60min, or LJ-00min (the generator before it heard her).
5. The restored recording plays; Play again; the workflow goes round again: choose the same recording and another network to compare.

How it works: the speech given for modelling is named by the network; Speech Model Creation gives the network trained offline on those minutes of speech (an ML Model Object whose data is the network, by reference); Text To Speech says each lost text with it; Speech Restoration Assembly puts the pieces in place.

## Stand-alone

```
SrsApp --network LJ-05min --voice a.wav [b.wav ...] --damaged d.wav --sections 0.5-1.0,2.0-2.5 --say "first text" --say "second text" --out restored.wav
SrsApp --voice a.wav [b.wav ...] --damaged d.wav --sections ... --say ... --out restored.wav [--stand-in]
```
`--network` names a network of `Models\SRS-LJ` (`LJ-00min` ... `LJ-60min`); `--voice` is speech of the reader. Without `--network`, the voice is the OpenVoice tone colour of the `--voice` speech (needs `Data\openvoice` and `Data\pyannote-venv`, see `AIMs/CAE1/V2.5/Voice`); `--stand-in` speaks with a tone and needs no voice engine. Files are 16-bit PCM WAV; another rate is brought to 16 kHz.

## The networks

Made by `Test\Tools\SrsLj` (N-27 of `Night\TODO.md`): the base is the Piper checkpoint "kristin medium" (rhasspy/piper-checkpoints, a LibriVox reader, public domain), fine-tuned with OHF-Voice/piper1-gpl (GPL-3.0, a tool, not part of this software) on nested sets of LJ Speech 1.1 (public domain), the same training budget for the five. The sets and the test material: `Test\Data\Speech\LJSpeech\README.md`. Each network in `Models\SRS-LJ\<name>`: `<name>.onnx`, `<name>.onnx.json`, the last checkpoint, the log, `times.txt`.
