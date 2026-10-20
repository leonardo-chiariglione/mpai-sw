# MPAI reference software

Reference software for the standards of MPAI - Moving Picture, Audio and Data Coding by Artificial Intelligence (<https://mpai.community>), written for the
AI Framework **MPAI-AIF V3.0**: AI Modules (AIMs) with their Metadata, Controllers that run them, User Agents that run workflows, and Applications built on them.
Contact: secretariat@mpai.community. Licence: BSD-3-Clause (`LICENSE`); what others made and is included is in `THIRD-PARTY-NOTICES.md`.

This is reference and demonstration software. Where an AIM stands in for a technology the standard leaves to the Implementer (a neural network, a film reader), the code says so.

## What is in it

| Folder | What |
|---|---|
| `schemas` | The JSON Schemas of the standards: Data Types, and the Metadata (L2) of the AIMs (also on <https://schemas.mpai.community>) |
| `AIF` | The AI Framework: Controller and its API, AIM host, Port Data, Trust, Storage, and the User Agent (`UserAgent`: the workflow interpreter and devices, the clients) |
| `AIMs` | The AIMs, by standard (CAE, MMC, OSD, PAF, AIH, NNW, ...), and `AIMs/AMDs`: the Metadata (L3) of every AIM and Module that has code |
| `Apps` | The Applications: Thalia (MAS-App with MAD, AMQ, MAT, MPD), ABV (avatar videoconference), AIH (health), CAE (Audio Scene Management, Speech Restoration, Emotion Enhanced Speech, Audio Recording Preservation, Enhanced Audioconference Experience), CAV (connected autonomous vehicle), MMC standalone programs |
| `Store` | The MPAI Store: the repository of AIMs and the checks a Controller makes of what it obtains |
| `MMM`, `MKG` | The metaverse meeting demonstration; the MPAI Knowledge Graph and the scripts that build it |
| `Test` | The tests (xUnit) and the recorded states they compare with (`Test/Expected`) |
| `AllApps` | Windows launchers, one double-click for each program |
| `docs` | Guides and design notes |

## Build and test

.NET 10 SDK, Windows (several programs use Windows Forms or WPF; the Service also has a Linux guide). 

```
dotnet build Test/Mpai.Aif.Tests/Mpai.Aif.Tests.csproj
dotnet test  Test/Mpai.Aif.Tests --filter "Group=Fast"      # minutes
dotnet test  Test/Mpai.Aif.Tests --filter "Group=Slow"      # starts programs and engines: run it when you can wait
```

The AI models (speech, vision, OCR, language, voice) are not in the repository: `docs/MAS-App-Models.md` and `docs/models-provenance.md` say what each is and where to get it. Without them the tests that need them are skipped.

## Where to start

- `docs/MPAI-Software.md`: an overview, and the ideas in one page (AIM, Module, Controller, User Agent, Port addressed by Data Type and Port Number).
- `docs/MAS-App-User.md`, `docs/MAS-App-Developer.md`: using and extending Thalia.
- `Apps/CAE/<App>/README.md`: each CAE-USC App, with how to run it; `AllApps/README.txt` lists every launcher.