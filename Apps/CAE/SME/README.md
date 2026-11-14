# Speak Model with Emotion

Gives emotionless speech the emotion of a model utterance, by its prosody (CAE-SME of CAE-USC V2.5).

| Part | What |
|---|---|
| The App | `SmeApp` (`App\`): the program you run; it starts the Module and runs the User Agent |
| User Agent | `AIF/UserAgent/Ua` (`Mpai.Ua`): reads `SME.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM and no use case |
| Controller | the Controller of the Module, in the App's process, holding no AIM (`Apps/CAE/Support`, `ModuleHost`) |
| Module | the AIMs of `SME` (Speech Feature Analysis 1, Prosodic Emotion Insertion), on an AIM host (`AIF/AimHost`), a process of its own, started and ended by the App |

The App runs stand-alone (the program above) and in the MAS App (an App of the MPAI-MAS Service, `Apps/MAS/Service/mas-server-AI.json`), with ONE workflow, `SME.orch`: the avatar says what the App does and what to do; the person speaks into the microphone or chooses a WAV file, then gives the model utterance the same way; the speech with the emotion is played, with a Play again button. The Mode Selector is written in the workflow as an instance of its Data Type (AIF V3.0, Workflow Description, 3). The stand-alone program answers the workflow with the files of its command line.

## Use

```
SmeApp --speech neutral.wav --model model.wav --out out.wav
```
