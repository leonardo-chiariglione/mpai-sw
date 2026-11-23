# All the Apps that run

Double-click the one you want: each one starts what it needs. The first start of a program builds it, which takes a little longer.
Each standard is a folder; the Apps of a standard are the files in it, named as the App. `_support` holds what the launchers share (settings, Ollama, the Service); the software is the folder above `AllApps`.

| Folder | App | What it does |
|---|---|---|
| `CAE` | `ASM` | Audio Scene Management by voice: speak or type to an audio scene; it starts Ollama itself (README: `Apps\CAE\ASM\README.md`) |
| | `EAE`, `SRS`, `AES`, `SME`, `EES`, `ARP` | the Apps of CAE-USC, with dialogs to pick the files: EAE the audio of a microphone array, SRS restores damaged speech, AES adds an emotion you name to speech, SME gives speech the emotion of a model utterance, EES does either (you choose a model utterance, or name an emotion), ARP preserves an open reel tape (results in `Downloads\MPAI USC`) |
| | `EAE server (virtual room)` | a virtual room of real speech; open http://localhost:5190/ with headphones |
| | `SRS demo (one click)` | restores a damaged recording with the voice of another speaker; plays original, damaged, restored |
| `MMC` | `ACR`, `MAC`, `MCA`, `MTS`, `AMC`, `AMQ`, `NPR` | the standalone programs: access registration, access control, conversation with the avatar, translation, affective conversation, picture questions, newspaper reading |
| | `CPS` | conversation with personal status: you pick a picture of your face and say what you say (typed, or a WAV file); the machine answers with a Portable Avatar, taking into account how you feel (answers in `Downloads\MPAI MMC\CPS`; it starts its own Controller on port 5059) |
| | `CAS` | conversation about a scene: you pick a picture of a scene and a WAV file of what you say about it (as many turns as you like); the machine answers with a Portable Avatar (answers in `Downloads\MPAI MMC\CAS`; it starts its own Controller on port 5061) |
| `OSD` | `TMA` | television media analysis: you pick the video of a programme (and how much of it); it tells who speaks when, what is said and what is shown (answers in `Downloads\MPAI OSD\TMA`; it starts its own Controller on port 5060) |
| `CUI` | `CPP` | company performance prediction: you pick the file of a company (`Apps\CUI\CPP\Samples` has a healthy and a distressed one) and the horizon; it gives the probability of default and of discontinuity and which figures weigh most (a demonstration model trained on Polish companies, not for real companies; report in `Downloads\MPAI CUI\CPP`; it starts its own Controller on port 5063) |
| `CAV` | `CAV`, `HCI` | the connected autonomous vehicle; the passenger conversation with it |
| `ABV` | `ABV server (this PC only)`, `ABV server (open to the network)` | the avatar videoconference |
| `MMM` | `MMM demo`, `MMM world`, `MMM meeting`, `Stop the MMM meeting` | the metaverse meeting: the demo, a conference with people outside this network, personal links |
| `XRV` | `LTP-demo` | Live Theatrical Performance (XRV-LTP of MPAI-XRV V1.0) live on the screen: it starts the two M-Instances of MMM-TEC (the Theatre, the Real Environment, and the Square, the Virtual Environment), the Controller with the Module of XRV-LTP and its User Agent, and opens the two Venues full screen; press Start, and Close to end (`Apps\XRV\LTP\README.md`) |
| `AIH` | `HSP demo` | the first slice of AIH-HSP, narrated |
| `AIF` | `AI Framework` | the AI Framework as its User sees it: Thalia, the MPAI AI Framework user environment - a full-screen screen with the local and remote search, the Apps and what is running (it builds what changed first; `Thalia\Thalia.bat`) |
| | `Store service`, `Store app` | the repository of AIMs; submit an AIM |
| `PGM` | `A-User door` | the A-User App of PGM-AUA (M3313): in the M-Instance of a room with one door you tell the A-User, by voice or in writing, to open the door when the bell rings and greet whoever is there; the Visitor rings, the A-User walks to the door, opens it and says welcome (it starts Ollama, the M-Instance and the Host, and opens the page; its User Agent is `MMM\Server\DoorUseCase.cs`, its Module `1PGM-ATU-V1.0-I01`) |
| `MAS` | `MAS App desktop client`, `MAS App browser server`, `Stop the MAS App and the Service`, `MAS App local apps` | The MAS App, the MAS launcher: you choose an App in its window; `MAS App local apps` lets its icons start the standalone MMC Apps |
| `_retired` | `ASM typed (old window)` | the first ASM window, typed commands; it cannot be built any more |

The address of the Service is in `_support\_settings.bat`.
