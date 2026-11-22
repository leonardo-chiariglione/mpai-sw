# Television Media Analysis

Analyses a television programme (OSD-TMA of MPAI-OSD V1.5): from its pictures and its sound, and an auxiliary text if there is one, it gives the Multimodal Event Descriptors - the scenes of the programme, who speaks when, what is said, which faces are shown.

| Part | What |
|---|---|
| The App | `TmaApp` (`App\`): a User Agent. It reads `OSD-TMA.orch`, gets each datum from where the App says, and talks to the Controller through the Controller API only; it knows no AIM. It splits the video as TV Splitting would (no implementation of it exists yet): the pictures, 5 a second at 960 x 540, and the sound, 16 kHz mono, with ffmpeg |
| Controller | `TmaController` (`Controller\`): a program of its own, on port 5060 of this PC, which starts the Module and its AIMs as the L3 says, with the models of `Models\` |
| Module | `1OSD-TMA-V1.5-I01`: visual change detection, audio segmentation, full to basic object, face and speaker recognition, speech recognition, identity reconciliation, speech and visual scene description, scene multiplexing, event description |
| Provider | `TmaProvider` (`Provider\`): builds the AIMs of the Module |

The people TMA can name are those registered in a gallery (the folder named by the environment variable `MPAI_TMA_GALLERY`, where Access Control Registration registered them); with none, TMA says who speaks when, not who the people are.

## Use

```
TmaController                                   (once; listens on http://127.0.0.1:5060)
TmaApp --video programme.mp4 [--seconds 30] [--text "what it is about"] [--idle 150] --out folder
```

The analysis takes several times as long as the part analysed. The Event Descriptors grow as the scenes arrive and the last holds them all: the App writes it as `event-descriptors.json` and says how many scenes it holds and who said what, once no new one has come for `--idle` seconds.

From `AllApps\OSD\TMA.bat`: dialogs pick the video, ask how many seconds and for the auxiliary text; the result goes to `Downloads\MPAI OSD\TMA`.

OSD-TXO-V1.5 and OSD-MEV-V1.5 travel between the App and the Controller as the JSON instance of their schema (`AIF\PortData`).
