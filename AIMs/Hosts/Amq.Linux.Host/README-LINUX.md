# Running the AIMs on Linux

The AI Framework, the three central AIMs (ASR, TIQ, TTS), the acquisition and
delivery AIMs used here, and the AMQ composite are all platform-neutral
(`net10.0`). Nothing Windows-specific is referenced by this host.

## 1. Copy two folders

    AIMs/     the AI Modules, the AMD metadata repository, this host
    AIF/      the AI Framework

Keep them side by side, as on Windows:

    ~/ai/AIMs
    ~/ai/AIF

## 2. Install the .NET 10 SDK

    dotnet --version        # must report 10.x

## 3. Copy the models and tools

| What      | Notes                                                       |
|-----------|-------------------------------------------------------------|
| BLIP ONNX | the three .onnx files AND blip_vision_model.onnx.data        |
| vocab.txt | from blip-vqa-base                                          |
| Whisper   | a LINUX build of whisper-cli, plus ggml-base.en.bin          |
| Piper     | a LINUX build of piper, plus the voice .onnx and .onnx.json  |

The .onnx model files are platform-independent: copy them unchanged.
The whisper and piper EXECUTABLES are not: download the Linux builds.

    chmod +x /path/to/whisper-cli /path/to/piper

## 4. Edit aim-settings.json

Every path in this file must exist. Linux is case-sensitive: `Zebra.jpg` and
`zebra.jpg` are different files.

Provide the two inputs the file-based run needs:

    an image             ImageFile
    a spoken question    QuestionAudio   (a WAV, 16 kHz mono is ideal)

## 5. Run

    cd ~/ai/AIMs/Hosts/Amq.Linux.Host
    dotnet run

Expected output:

    MMC-AMQ-V2.5 on AIF (Linux)   [files only]
    Hierarchy from Metadata: ...
    Execution plan (top level): CVE-VOA-V1.0 -> CAE-AOA-V1.0 -> ...
    [CVE-VOA] acquired image: ...
    [CAE-AOA] acquired audio: ...
    [CAE-AOD] delivered N bytes -> .../<id>.wav
    Result: OSD-BAO-V1.5

Other runs:

    dotnet run -- MMC-AMQ2-V2.5     the hierarchical composite
    dotnet run -- --devices         ALSA microphone and loudspeaker

## 6. If something fails

Failures are reported, not thrown. The message names the AIM:

    Failed at CAE-AOA-V1.0: Audio source not found: /home/you/ai/audio/question.wav

so fix that one path and run again.

| Symptom                                   | Cause                                   |
|-------------------------------------------|-----------------------------------------|
| Vision model fails to load                | the .onnx.data sidecar was not copied   |
| whisper or piper "not found"              | wrong path, or not chmod +x, or a Windows binary |
| TIQ produces nothing                      | vocab.txt path wrong                    |
| `--devices` fails                         | install alsa-utils (arecord, aplay)     |

## What has NOT been tested

This host has never been built or run on Linux. The file-based path uses only
code that runs today on Windows, so it is the safest first attempt. The two
ALSA implementations (`AlsaAudioAcquisition`, `AplayAudioDelivery`) have never
been executed at all - try `--devices` only after the file-based run works.

