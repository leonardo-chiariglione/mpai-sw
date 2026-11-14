# Add Emotion to Speech

Gives emotionless speech an emotion that is named, at a degree (CAE-AES of CAE-USC V2.5).

| Part | What |
|---|---|
| The App | `AesApp` (`App\`): the program you run; it starts the Module and runs the User Agent |
| User Agent | `AIF/UserAgent/Ua` (`Mpai.Ua`): reads `AES.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM and no use case |
| Controller | the Controller of the Module, in the App's process, holding no AIM (`Apps/CAE/Support`, `ModuleHost`) |
| Module | the AIMs of `AES` (Speech Feature Analysis 2, Emotion Feature Production, Neural Emotion Insertion), on an AIM host (`AIF/AimHost`), a process of its own, started and ended by the App |

The App runs stand-alone (the program above) and in the MAS App (an App of the MPAI-MAS Service, `Apps/MAS/Service/mas-server-AI.json`), with ONE workflow, `AES.orch`: the avatar says what the App does and what to do; the person speaks into the microphone or chooses a WAV file, then chooses an emotion from the list of MPAI-MMC and sets how strong with a slider; the speech with the emotion is played, with a Play again button. The Mode Selector and the Language Selector (English) are written in the workflow as an instance of its Data Type (AIF V3.0, Workflow Description, 3). The stand-alone program answers the workflow with the files of its command line.

## Use

```
AesApp --speech neutral.wav --emotion SADNESS [--degree 0.5] --out out.wav
```

The neural features are produced by a rule table (a stand-in for a neural network); see `MKG/pending-changes.md`.
