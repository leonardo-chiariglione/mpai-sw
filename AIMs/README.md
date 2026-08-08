# MPAI AIMs

A monorepo of MPAI AI Modules (AIMs). Each AIM is an independent project that
depends only on the shared `Mpai.Core`; a composite AIM combines other AIMs by
wiring their interfaces.

## Layout

```
AIMs/
├── AIMs.sln                 build everything
├── Core/                    Mpai.Core — data objects, qualifiers, AIM interfaces
├── CVE/
│   └── V1.0/
│       └── VOA/             Visual Object Acquisition  (CVE-VOA-V1.0)
├── CAE3/
│   └── V1.0/
│       ├── AOA/             Audio Object Acquisition   (CAE-AOA-V1.0)
│       └── AOD/             Audio Object Delivery      (CAE-AOD-V1.0)
├── MMC/
│   └── V2.5/
│       ├── ASR/             Automatic Speech Recognition  (MMC-ASR-V2.5)
│       ├── TIQ/             Text and Image Query          (MMC-TIQ-V2.5)
│       ├── TTS/             Text to Speech                (MMC-TTS-V2.5)
│       └── AMQ/             Answer to Multimodal Question (MMC-AMQ-V2.5, composite)
└── Host/                    Amq.Host — a program that RUNS the AMQ composite
```

Basic and Composite AIMs sit at the same level inside a standard/version folder.
`AMQ` is a composite: it contains five SubAIMs (AOA, ASR, TIQ, TTS, AOD).

## Dependencies

- `Mpai.Core` depends on nothing.
- Every AIM depends only on `Mpai.Core`.
- `AMQ` depends only on `Mpai.Core` too — it uses the AIM *interfaces*, not the
  concrete SubAIM projects. The **Host** references the concrete projects and
  injects the platform edges, so `AMQ` stays portable.

## Target frameworks

- Portable (net10.0): Core, ASR, TIQ, TTS, AMQ.
- Windows (net10.0-windows, NAudio): AOA, AOD, Host — the device edges.
- Linux: build the edges with the ALSA/aplay classes (already present:
  `AlsaAudioAcquisition`, `AplayAudioDelivery`), retarget those projects and
  the Host to net10.0, and drop NAudio.

## Build & run

From the repo root:

```
dotnet build AIMs.sln
dotnet run --project Host/Amq.Host.csproj
```

Put an `image.jpg` in the folder you run from. The model/tool paths (BLIP,
Whisper, Piper) are set at the top of `Host/Program.cs`.

## Git / GitHub / GitLab

- **GitHub** hosts this repo for active AIM development (monorepo).
- **GitLab** hosts the MPAI standards, and will also hold AIMs "static" — a
  snapshot pushed once development of a version is complete.
- `bin/` and `obj/` are git-ignored (never commit build output).

Suggested first steps:

```
cd <this folder>
git init
git add .
git commit -m "Initial import: MPAI AIMs monorepo (Core, CAE3 AOA/AOD, MMC ASR/TIQ/TTS/AMQ, Host)"
git branch -M main
git remote add origin <your GitHub repo URL>
git push -u origin main
# later, a static snapshot to GitLab:
git remote add gitlab <MPAI GitLab repo URL>
git push gitlab main
```

Keep working copies OUTSIDE OneDrive-synced folders to avoid file-lock/stale-build issues.
