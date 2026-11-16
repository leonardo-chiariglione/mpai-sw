# Live Theatrical Performance

Runs a show in a Real and a Virtual venue at once (XRV-LTP of MPAI-XRV V1.0). Cue by Cue, the Script decides what the lights, the sound and the virtual stage do, as the performers, the participants and the operators act.

| Part | What |
|---|---|
| Workflow | `XRV-LTP.orch`: one Controller, one Module, the Live Theatrical Performance Composite AIM `1XRV-LTP-V1.0-I01`, run continuously |
| User Agent | `AIF/UserAgent/Ua` (`Mpai.Ua`): reads `XRV-LTP.orch`, binds the devices Show, RealVenue and VirtualVenue to the boundary Ports, and talks to the Controller through the Controller API only |
| Module | the seven AIMs of XRV-LTP (`AIMs/XRV1/V1.0`): Environment Description, Performance, Participant and Operator Status Interpretation, Action Generation, RE and VE Experience Generation |
| Output Units | the devices that execute RE Data Out and VE Data Out. In this first slice they are stand-ins, in the test only (`Test/Mpai.Aif.Tests/XrvLtpWorkflowTests.cs`): a DMX writer for the lighting. The virtual venue is MMM-TEC V2.2 (`VirtualVenue/MmmVirtualVenue.cs`, over the MMM-API of `D:\AI\MMM`): the virtual stage is an M-Location of an M-Instance and the show is a collection of Processes in it (the Users of performers and participants, and whatever animates Items); the device is the Process that animates the virtual venue: it places the Virtual Objects with MM-Add, sets a lamp with Property Change (Light Source, Intensity) and gives as VE Data In who comes onto the stage, from MM-Capture. No real hardware, no MIDI |

## The devices

| Device | Gives (stream) | Receives (deliver) |
|---|---|---|
| Show | Script, Action Table, Script-Venue Associations, RE and VE Venue Specifications, Participant Data | Participant Data |
| RealVenue | RE Data In | RE Data Out |
| VirtualVenue | VE Data In | VE Data Out |

## Not yet

- The App program (an executable with its own AIM host, like the other Apps): not written. The workflow runs in the tests and in the demo `TwoMetaverses`.
- Real Output Units (a DMX interface, MIDI, stage machinery). The virtual venue on MMM-TEC changes only Intensity (Light Source); other VE properties are not performed (said in Unperformed).

## Shows to simulate

- The mini show (`Test/Data/XRV/MiniShow`): three Cues, Real, Virtual, Mixed.
- The Lamplighter (`Test/Data/XRV/Lamplighter/Script.json`, with the other documents of the mini show): two Cues, machine-driven, no operator. C1 (Mixed): Anna on the real stage and a guest on the virtual stage - real lamps up, virtual lamp at 30%. C2 (Virtual): Bot, the virtual double of Anna, an MMM Process that walks onto the virtual stage by itself a second after it sees the lamp lit - real lamps off, virtual lamp full; the show ends.
- The longer evening in Real-Virtual Environments (`Test/Data/XRV/LongerEvening`, made by `Test/Data/XRV/make_longer_evening.py`, 2026-11-15): seven Cues (C0-C5, C6a; END ends the show). BOTH environments are M-Instances of MMM-TEC: the Real Environment "Theatre" (Anna; the audience only heard: its applause is an Audio Object; the operator's console) and the Virtual Environment "Square" (Guest1-3, Bot). Each is driven by its own list of time-stamped Process Actions (`ProcessActions-Theatre.json`, `ProcessActions-Square.json`: MM-Add, MM-Move, MM-Animate with an Animation Stream of Body Descriptors, Property Change); Bot also reacts (turns to the portal when it brightens). The performers speak in turn, never two voices at once: Anna in the Theatre, Bot in the Square, the guests in the gaps; each line is an Audio Object (OSD-AUO-V1.5) added with MM-Add by the speaker's own Process, with a Script-Venue Association. The Status Interpretation AIMs read the body (raising arms, sitting), the applause (Clapping Intensity) and the facing (Spatial Attitude) in the Descriptors (`AIMs/XRV1/V1.0/Common/Behaviour.cs`).

## The demo in Real-Virtual Environments (`TwoMetaverses`)

`dotnet run --project Apps/XRV/LTP/TwoMetaverses -- [--scale 1] [--out <folder>] [--no-video] [--fps 20]`. It starts the two M-Instances, the User Agent with this workflow and three devices (the Show; `VirtualVenue/MmmVenue.cs` for the Theatre as RealVenue and for the Square as VirtualVenue: DMX and {Object, Property, Value} commands performed with Property Change; RE/VE Data In from MM-Capture and the Animation Streams it watches), plays the two lists of Process Actions live, records both M-Instances every 100 ms with the Data Out the Module delivered (its DescrMetadata says the Cue and the Condition the Module found met), and draws the recording with `stage/index.html` (three.js, the Personae of the MMM viewer) in headless Edge, frame by frame, into an MP4 with the applause (ffmpeg). `--render-only` draws a recording again; `--stills t1,t2` gives pictures. As in the tests, the Controller runs in the same program (the User Agent uses the Controller API only).

`--live [--port 8771] [--lead -1]` shows the two Venues on the screen in real time instead of a video: the page polls the state of the show, a Start button begins it (and allows the sound), a Close button ends it. The Theatre keeps its own scene (a 14 m stage, red curtain); its scrim shows only the Square's performers and the Square's portal only the Theatre's, so the two Venues are never the same picture; the audience is heard, not drawn.
