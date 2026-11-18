# MPAI-MMM V2.2 - Technologies (MMM-TEC)

The implementation of MMM-TEC V2.2 on this repository: an M-Instance, its Processes and
Items, operated through the MMM-API. The first target is the Baseline Profile and a
stripped-down Use Case 2.

| Folder | What it holds |
|---|---|
| `Api` | `MMM-API.json`, the MMM-TEC V2.2 API as OpenAPI 3.1, built from the schemas by `build_openapi.py`, with its report `MMM-API-report.txt` |

## Demonstration: "Open the door when the bell rings" (M3313)

`AllApps\MMM\Run-Door.cmd` starts the M-Instance of a room with one door and opens `/viewer/door.html`. The Host User is an
A-User App: its User Agent (`Server/DoorUseCase.cs`) and the A-User Module of PGM-AUA (`1PGM-ATU-V1.0-I01`, the AIMs of
`AIMs/PGM/V1.0`) that the AIF Controller runs, reached through the Controller API only. The human writes (or speaks:
Whisper) "Open the door if a bell rings and greet whoever is at the door". Basic Knowledge (Ollama, `llama3.2:3b`) finds
the Condition; A-User Control keeps the goal pending and asks Context Description to report the Audio Event; the User
Agent gives the Module every sound its Persona hears (MM-Capture of the room); Context Description recognises the bell
(a knock is not a bell); Basic Knowledge is asked again, the Condition held; A-User Control requests MM-Animate of
the Persona At the Doorway With the Portable Avatar of the walk (a Persona walks with MM-Animate, never MM-Move), then -
when the M-Instance says the walk is over - Property Change of the Door (Item Property OpenState Open, MMM-IPR-V3.0),
then asks Basic Knowledge for the words that follow and requests MM-Animate With the Portable Avatar that says "Welcome". The room and its sound are rendered to the human
(MU-Actuate). Test: `Test/Mpai.Aif.Tests/PgmConditionTests.cs`.

## Demonstration: Use Case 2, "Friends meet in the metaverse"

`AllApps\MMM\Run-Demo.cmd` (it runs `Run-Demo.ps1` without needing PowerShell's script policy
changed) starts an M-Instance and its 3-D viewer and opens the viewer in the browser; the
presenter shows the 19 steps of the Use Case: **Space, up arrow or a click** for the next
step, **down arrow** for the previous one (the scene as it was shown; nothing is undone in the
M-Instance). `AllApps\MMM\Run-Demo.cmd 4` plays them by itself, 4 s a step. Friend1 operates from the
Universe, so nothing of it is drawn until step 6, when its captured data has been identified;
Friend2 is already at its place when the Use Case begins. Everything is local: the
viewer needs no internet (its three.js is in `Viewer/lib`, MIT licence).

| Step | What the viewer shows | Process Action |
|---|---|---|
| 1 | human1 registers | Register |
| 2, 3 | Friend1 buys the Parcel, then the Room | Transact |
| 4 | Friend1's Persona appears at Metaverse Square | MM-Add |
| 5, 6 | Friend1's camera data is captured and identified | UM-Capture, Identify |
| 7 | the Persona is animated | MM-Animate |
| 8 | Friend1 signals presence | MM-Send |
| 9 | the Persona walks to the Parcel | MM-Move |
| 10, 11 | the Room is placed on the Parcel, then made perceptible | MM-Add, Property Change |
| 12 | the Persona enters the Room | MM-Move |
| 13 | the Room is rendered for human1 | MU-Actuate |
| 14, 15 | Friend1 invites Friend2, Friend2 accepts | MM-Send |
| 16 | Friend1 grants Friend2 access to the Room (before it, Friend2's attempt to enter is refused: 403) | Rights Change |
| 17 | Friend2's Persona walks into the Room | MM-Move |
| 18 | Friend2 leaves the Room | MM-Move |
| 19 | Friend1 revokes the access: Friend2's next entry is refused (403) | Rights Change |

What this is, and is not: the 19 steps and their order are those of the specification
(MMM-TEC V2.2, Verification Use Cases, Use Case 2). It uses 11 Process Actions; the
Baseline Profile as the specification lists it has 8, and Register, Transact, Property
Change and Rights Change are not among them - so this is Use Case 2 as a whole, not a
Baseline-Profile-only demonstration. It is the author's stripped-down implementation of
the M-Instance (no Marketplace, no other Use Case), and the viewer draws only Locations
and Personae.

The schemas are those of `schemas/MMM4/V2.2` (`data` and `actions`) and those they refer
to; they are the published ones, from the AI tree. The API is made non-recursive when it is
built, not in the schemas:

1. a Data Type reached inside a Data Exchange Metadata is taken without its own
   `DataXMData`;
2. where a schema refers back to one it is part of, the inner reference is the
   identifier of the Item, read at `GET /api/v2.2/items/{itemID}`.

`Test/Mpai.Aif.Tests/MmmApiTests.cs` checks that no component schema reaches itself and
that every `$ref` resolves. After a change to the schemas:

```
python AllApps/MMM/Api/build_openapi.py
```
