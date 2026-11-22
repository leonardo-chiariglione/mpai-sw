# Conversation with Personal Status

A conversation with a machine that takes the personal status of the person into account (MMC-CPS of MPAI-MMC V2.5): from the face, the voice and the words of the person it gives the Personal Status, and the machine answers with a Portable Avatar that says its answer and shows its own state.

| Part | What |
|---|---|
| The App | `CpsApp` (`App\`): a User Agent. It reads `MMC-CPS.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM |
| Controller | `CpsController` (`Controller\`): a program of its own, on port 5059 of this PC, which starts the Module and its AIMs as the L3 says |
| Module | `1MMC-CPS-V2.5-I01`: Multimodal Frontend Description, Entity Body Description, Visual Object Identification, Automatic Speech Recognition, Natural Language Understanding, Personal Status Extraction, Entity Dialogue Processing, Portable Avatar Multiplexing |
| Provider | `CpsProvider` (`Provider\`): builds the AIMs of the Module with the providers that build them for the Communicating Entities in Context (`AIMs/HMC/V2.2/Provider`) |

The language model of Entity Dialogue Processing is Ollama's (`AllApps\_support\_ollama.bat` starts it).

## Use

```
CpsController                                   (once; listens on http://127.0.0.1:5059)
CpsApp --face face.jpg --text "words" [--text ... | --speech said.wav ...] --out folder
```

Each `--text` or `--speech` is one turn, in the order given; `--face` is the picture of the face, one for each turn (the last serves the turns that remain). The Portable Avatar that answers each turn is written as `avatar-1.json`, `avatar-2.json` ...; the App also says what the machine says and how it feels.

From `AllApps\MMC\CPS.bat`: dialogs pick the picture, ask for the words (typed, or a WAV file) and offer another turn; the answers go to `Downloads\MPAI MMC\CPS`.

With `--camera` the face is what the Camera Unit of the Physical Layer sees each turn, and with `--microphone` what is said is what the Microphone Unit hears (`--turns` at most, 3 by default; it waits for speech and ends when the speaker stops, and if nobody speaks the conversation ends). Without them the face is a picture and what is said a WAV file or typed words; the two are alternatives, not mixed. The App is a Windows program for this reason (the Units are Windows' devices).

**The machine's voice.** The App says the answer aloud: the words of the Portable Avatar go, through the Controller API, to the Module that makes machine speech from text (`1PAF-RSR-V1.6-I01`, which the Controller of the App holds as it holds the Module of the App), and the speech that comes back is played on the Loudspeaker Unit of the Physical Layer; it is also kept as `answer-1.wav`, `answer-2.wav` ... in the folder of the answers. `--quiet` says nothing aloud and keeps the files. The code is `Apps\MMC\CPS\App\MachineVoice.cs`, shared by both Apps. The App shows no avatar on screen.
