# Conversation About a Scene

A conversation with a machine about a scene (MMC-CAS of MPAI-MMC V2.5): the person shows a scene and speaks about it, and about what the person indicates in it; the machine answers with a Portable Avatar that says its answer and shows its own state.

| Part | What |
|---|---|
| The App | `CasApp` (`App\`): a User Agent. It reads `MMC-CAS.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM |
| Controller | `CasController` (`Controller\`): a program of its own, on port 5061 of this PC, which starts the Module and its AIMs as the L3 says |
| Module | `1MMC-CAS-V2.5-I01`: Multimodal Frontend Description, Entity Body Description, Visual Object Identification, Automatic Speech Recognition, Natural Language Understanding, Personal Status Extraction, Entity Dialogue Processing, Portable Avatar Multiplexing |
| Provider | the one of Conversation with Personal Status (`Apps/MMC/CPS/Provider`): the front end of this Module is the same |

The language model of Entity Dialogue Processing is Ollama's (`AllApps\_support\_ollama.bat` starts it).

## Use

```
CasController                                   (once; listens on http://127.0.0.1:5061)
CasApp --scene scene.jpg [--scene other.jpg ...] --speech said.wav [--speech ...] --out folder
```

Each `--speech` is one turn, in the order given; `--scene` is the picture of the scene, one for each turn (the last serves the turns that remain). The Portable Avatar that answers each turn is written as `avatar-1.json`, `avatar-2.json` ...; the App also says what the machine says and how it feels.

From `AllApps\MMC\CAS.bat`: dialogs pick the picture and the WAV file of each turn and offer another turn; the answers go to `Downloads\MPAI MMC\CAS`.

With `--camera` the scene is what the Camera Unit of the Physical Layer sees each turn, and with `--microphone` what is said is what the Microphone Unit hears (`--turns` at most, 3 by default; it waits for speech and ends when the speaker stops, and if nobody speaks the conversation ends). Without them the scene is a picture and what is said a WAV file; the two are alternatives, not mixed. The App is a Windows program for this reason (the Units are Windows' devices). The View Selector and the Point of View of the Module are not given: the Module answers without them, as its test does.

**The machine's voice.** The App says the answer aloud: the words of the Portable Avatar go, through the Controller API, to the Module that makes machine speech from text (`1PAF-RSR-V1.6-I01`, which the Controller of the App holds as it holds the Module of the App), and the speech that comes back is played on the Loudspeaker Unit of the Physical Layer; it is also kept as `answer-1.wav`, `answer-2.wav` ... in the folder of the answers. `--quiet` says nothing aloud and keeps the files. The code is `Apps\MMC\CPS\App\MachineVoice.cs`, shared by both Apps. The App shows no avatar on screen.
