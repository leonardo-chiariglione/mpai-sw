"""Writes the longer evening of XRV-LTP V1.0 on two metaverses (N-26) into D:\\AI\\Test\\Data\\XRV\\LongerEvening.

The Real Environment is the M-Instance "Theatre", the Virtual Environment the M-Instance "Square" (MMM-TEC V2.2). Each is driven by its own list of
TIME-STAMPED PROCESS ACTIONS (ProcessActions-Theatre.json, ProcessActions-Square.json), performed live at their times by the demo
(Apps\\XRV\\LTP\\TwoMetaverses). The actors are Personae: Anna in the Theatre; Guest1-3 and Bot in the Square. The theatre audience is only heard:
its applause is an Audio Object added in the Hall (applause.wav, 16 kHz mono, synthesised here: many clappers, no recording).

Seven Cues (one ending played, group 1 of C5):
  C0 Real     the operator gives Go -> house lights down; dusk in the Square
  C1 Mixed    Anna onto the Theatre stage AND Guest1 onto the Square stage -> both real lamps full; virtual lamp 30%
  C2 Real     the Theatre applauds (RE Participant Status, Audio Behaviour, Clapping Intensity) -> lamp 2 amber, spot on Anna; the applause heard in the Square
  C3 Virtual  the guests raise their arms (VE Participant Status, Gesture "Raising arms") -> fireflies around the virtual lamp (and on the scrim)
  C4 Virtual  Bot walks onto the Square stage -> real lamps off; virtual lamp full
  C5 Real     group 1: Anna raises her arms (Gesture) -> the scrim brightens, the portal of the Square brightens (Bot turns to it) -> C6a
              group 2: Anna sits down (StandSit false) -> slow fade to black, the virtual lamp fades -> END (in the Script, not played)
  C6a Real    Anna turns to face Bot on the scrim (Spatial Attitude) -> all lamps full, confetti; fireworks over the Square -> END
  END         a Cue with no group: reaching it, the show has ended.
Run: python -I make_longer_evening.py
"""
import base64, json, math, os, random, struct, wave

UE_T, UE_S = "UE-Theatre", "UE-Square"
UE = "UE-Evening"           # the U-Environment of the show (the Script, the Action Table...)
MI_T, MI_S = "Theatre", "Square"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "LongerEvening")


def t(name, ms=0):
    return {"Header": "OSD-STM-V1.5", "SimpleTimeID": name,
            "SimpleTimeData": [{"FlagsByte": 3, "StartTime": ms, "EndTime": ms, "AccuracyMode": "single", "AccuracyPlusMinus": 0, "TimeType": True, "TimeUnit": "01"}]}


def status(key, header, ident, data):
    return {"Header": header, key + "ID": ident, "UEnvironmentID": UE, key + "Time": t(ident + "-t"), key + "Data": data}


def vb(ident, behaviour):
    return {"Header": "XRV-VBH-V1.0", "VisualBehaviourID": ident, "UEnvironmentID": UE, "VisualBehaviourTime": t(ident + "-t"), "Behaviour": behaviour}


def ab(ident, data):
    return {"Header": "XRV-ABH-V1.0", "AudioBehaviourID": ident, "UEnvironmentID": UE, "AudioBehaviourTime": t(ident + "-t"), "BehaviourData": data}


def attitude(ident, x, y, yaw):
    return {"Header": "OSD-OSA-V1.5", "ObjectSpatialAttitudeID": ident,
            "Position": {"Header": "OSD-OPS-V1.5", "PositionID": ident + "-p", "CartPosition": [x, y, 0]},
            "Orientation": {"Header": "OSD-OOR-V1.5", "OrientationID": ident + "-o", "Orientation": [yaw, 0, 0]}}


arms_up = lambda i: vb(i, {"Gestures": [{"Gesture": "Raising arms"}]})

# --- the targets of the Cue Conditions
anna_on = status("REPerformanceStatus", "XRV-RFS-V1.0", "C1-re", {"IndividualStatuses": [{"REPerformerID": "Anna"}]})
guest_on = status("VEParticipantStatus", "XRV-VTS-V1.0", "C1-ve", {"IndividualStatuses": [{"VEParticipantID": "Guest1"}]})
applause = status("REParticipantStatus", "XRV-RTS-V1.0", "C2-re", {"IndividualStatuses": [{"REParticipantID": "Audience", "AudioBehaviour": ab("C2-ab", {"ClappingIntensity": 60})}]})
guests_up = status("VEParticipantStatus", "XRV-VTS-V1.0", "C3-ve", {"IndividualStatuses": [
    {"VEParticipantID": g, "VisualBehaviour": arms_up(f"C3-{g}")} for g in ("Guest1", "Guest2", "Guest3")]})
bot_on = status("VEPerformanceStatus", "XRV-VFS-V1.0", "C4-ve", {"IndividualStatuses": [{"VEPerformerID": "Bot"}]})
anna_up = status("REPerformanceStatus", "XRV-RFS-V1.0", "C5a-re", {"IndividualStatuses": [{"REPerformerID": "Anna", "VisualBehaviour": arms_up("C5a-vb")}]})
anna_sits = status("REPerformanceStatus", "XRV-RFS-V1.0", "C5b-re", {"IndividualStatuses": [{"REPerformerID": "Anna", "VisualBehaviour": vb("C5b-vb", {"StandSit": False})}]})
anna_turns = status("REPerformanceStatus", "XRV-RFS-V1.0", "C6a-re", {"IndividualStatuses": [{"REPerformerID": "Anna", "SpatialAttitude": attitude("C6a-sa", 0, 1, 0)}]})

cue_list = [
    {"Cue": [{"RealCue": {"RECueID": "C0", "REGroups": [{"REActions": ["RE-HOUSE-DOWN"], "VEActions": ["VE-DUSK"], "NextCue": "C1"}]}}]},
    {"Cue": [{"MixedCue": {"MECueID": "C1", "MEGroups": [
        {"RECueConditions": {"REPerformanceStatus": anna_on}, "VECueConditions": {"VEParticipantStatus": guest_on},
         "REActions": ["RE-LAMPS-FULL"], "VEActions": ["VE-LAMP-30"], "NextCue": "C2"}]}}]},
    {"Cue": [{"RealCue": {"RECueID": "C2", "REGroups": [
        {"RECueConditions": {"REParticipantStatus": applause}, "REActions": ["RE-AMBER-SPOT"], "VEActions": ["VE-APPLAUSE"], "NextCue": "C3"}]}}]},
    {"Cue": [{"VirtualCue": {"VECueID": "C3", "VEGroups": [
        {"VECueConditions": {"VEParticipantStatus": guests_up}, "REActions": ["RE-SCRIM-HALF"], "VEActions": ["VE-FIREFLIES"], "NextCue": "C4"}]}}]},
    {"Cue": [{"VirtualCue": {"VECueID": "C4", "VEGroups": [
        {"VECueConditions": {"VEPerformanceStatus": bot_on}, "REActions": ["RE-LAMPS-OFF"], "VEActions": ["VE-LAMP-FULL"], "NextCue": "C5"}]}}]},
    {"Cue": [{"RealCue": {"RECueID": "C5", "REGroups": [
        {"RECueConditions": {"REPerformanceStatus": anna_up}, "REActions": ["RE-SCRIM-FULL"], "VEActions": ["VE-PORTAL-FULL"], "NextCue": "C6a"},
        {"RECueConditions": {"REPerformanceStatus": anna_sits}, "REActions": ["RE-FADE-BLACK"], "VEActions": ["VE-LAMP-FADE"], "NextCue": "END"}]}}]},
    {"Cue": [{"RealCue": {"RECueID": "C6a", "REGroups": [
        {"RECueConditions": {"REPerformanceStatus": anna_turns}, "REActions": ["RE-FINALE"], "VEActions": ["VE-FIREWORKS"], "NextCue": "END"}]}}]},
    {"Cue": [{"RealCue": {"RECueID": "END", "REGroups": []}}]},
]

script = {
    "Header": "XRV-SCR-V1.0", "UEnvironmentID": UE, "ScriptID": "LongerEvening-Script", "ScriptTime": t("script-t"),
    "ShowHeader": "The longer evening", "TitleOfProduction": "The longer evening (two metaverses)",
    "ShowDescription": "Seven Cues on two metaverses: the Theatre (Real Environment) and the Square (Virtual Environment). Anna performs in the Theatre, Bot - her virtual double - and the guests in the Square. C5 is Anna's choice: arms up (played) or sitting down (the show fades out).",
    "Characters": [{"Name": "Anna", "Nickname": "A", "CharacterID": {"Real": "Anna", "Virtual": "Bot"}, "Mnemonic": "ANN"}],
    "TheatricalSetList": [{"TheatricalSet": [{"Name": "Stage", "SetID": {"Real": "TheatreStage", "Virtual": "SquareStage"}, "Mnemonic": "STG"}],
                           "Objects": [{"Name": "Lamp 1", "ObjectID": {"Real": "FX1", "Virtual": "VLamp1"}, "ObjectType": "Static", "Mnemonic": "L1"},
                                       {"Name": "Lamp 2", "ObjectID": {"Real": "FX2"}, "ObjectType": "Static", "Mnemonic": "L2"},
                                       {"Name": "Scrim", "ObjectID": {"Real": "Scrim", "Virtual": "Portal"}, "ObjectType": "Static", "Mnemonic": "SCR"}]}],
    "Participants": [{"MetaName": "Audience", "Nickname": "Hall", "Mnemonic": "AUD"}, {"MetaName": "Guest", "Nickname": "G", "Mnemonic": "GST"}],
    "Operators": [{"Consoles": [{"Name": "Show console", "Mnemonic": "CON"}]}],
    "CueList": cue_list,
}


def dmx(channel, value):
    return {"ActionCommand": {"Protocol": "DMX", "Universe": 1, "Channel": channel, "Value": value}, "ActionData": {}}


def prop(obj, prop_, value):
    return {"ActionCommand": {"Object": obj, "Property": prop_, "Value": value}, "ActionData": {}}


HOUSE, FX1, FX2, SPOT, SCRIM, CONFETTI = 1, 2, 3, 4, 5, 6
actions = [
    ("RE-HOUSE-DOWN", "House lights down", [dmx(HOUSE, 0)]),
    ("RE-LAMPS-FULL", "Both stage lamps full", [dmx(FX1, 255), dmx(FX2, 255)]),
    ("RE-AMBER-SPOT", "Lamp 2 amber, the follow spot on Anna", [prop("FX2", "Colour", "FFB000"), dmx(SPOT, 255)]),
    ("RE-SCRIM-HALF", "The scrim to half", [dmx(SCRIM, 150)]),
    ("RE-LAMPS-OFF", "Stage lamps and spot off", [dmx(FX1, 0), dmx(FX2, 0), dmx(SPOT, 0)]),
    ("RE-SCRIM-FULL", "The scrim full", [dmx(SCRIM, 255)]),
    ("RE-FADE-BLACK", "Fade to black", [dmx(FX1, 0), dmx(FX2, 0), dmx(SPOT, 0), dmx(SCRIM, 0)]),
    ("RE-FINALE", "All lamps full, confetti", [prop("FX2", "Colour", "FFFFFF"), dmx(FX1, 255), dmx(FX2, 255), dmx(SPOT, 255), dmx(CONFETTI, 255)]),
    ("VE-DUSK", "Dusk in the Square", [prop("Sky", "Intensity", 0.2)]),
    ("VE-LAMP-30", "Virtual lamp to 30%", [prop("VLamp1", "Intensity", 0.3)]),
    ("VE-APPLAUSE", "The applause of the Theatre heard in the Square", [prop("ApplauseSpeaker", "Intensity", 1.0)]),
    ("VE-FIREFLIES", "Fireflies around the lamp", [prop("ApplauseSpeaker", "Intensity", 0.0), prop("Fireflies", "Intensity", 1.0)]),
    ("VE-LAMP-FULL", "Virtual lamp full", [prop("VLamp1", "Intensity", 1.0)]),
    ("VE-PORTAL-FULL", "The portal to the Theatre full", [prop("Portal", "Intensity", 1.0)]),
    ("VE-LAMP-FADE", "The virtual lamp and the portal fade", [prop("VLamp1", "Intensity", 0.0), prop("Portal", "Intensity", 0.2)]),
    ("VE-FIREWORKS", "Fireworks over the Square", [prop("Fireworks", "Intensity", 1.0)]),
]
action_table = {
    "Header": "XRV-ATB-V1.0", "UEnvironmentID": UE, "ActionTableID": "LongerEvening-Actions", "ActionTableTime": t("atb-t"),
    "ActionTableData": [{"ActionID": a, "ActionName": n, "Action": c} for a, n, c in actions],
}

re_venue = {
    "Header": "XRV-RVS-V1.0", "MInstanceID": MI_T, "UEnvironmentID": UE_T, "REVenueSpecificationID": "Theatre-Venue", "REVenueSpecificationTime": t("rvs-t"),
    "DMXUniverses": [{"UniverseID": 1, "Protocol": "DMX", "Channels": [
        {"ChannelNumber": HOUSE, "ChannelDefinition": "Dimmer", "FixtureID": "House"},
        {"ChannelNumber": FX1, "ChannelDefinition": "Dimmer", "FixtureID": "FX1"},
        {"ChannelNumber": FX2, "ChannelDefinition": "Dimmer", "FixtureID": "FX2"},
        {"ChannelNumber": SPOT, "ChannelDefinition": "Dimmer", "FixtureID": "Spot"},
        {"ChannelNumber": SCRIM, "ChannelDefinition": "Dimmer", "FixtureID": "Scrim"},
        {"ChannelNumber": CONFETTI, "ChannelDefinition": "Effect", "FixtureID": "Confetti"}]}],
}


def model(ident):
    return {"Header": "OSD-3DO-V1.5", "MInstanceID": MI_S, "3DModelObjectID": ident, "3DModelObjectSpaceTime": t(ident + "-st")}


ve_venue = {
    "Header": "XRV-VVS-V1.0", "MInstanceID": MI_S, "UEnvironmentID": UE_S, "VEVenueSpecificationID": "Square-Venue", "VEVenueSpecificationTime": t("vvs-t"),
    "VirtualObjects": [model(i) for i in ("Sky", "VLamp1", "Portal", "Fireflies", "Fireworks", "ApplauseSpeaker")],
}


def assoc(name, ident, source=None, sink=None):
    a = {"Name": name, "VenueElementID": ident}
    if source: a["DataSourceQualifier"] = source
    if sink: a["DataSinkQualifier"] = sink
    return a


associations = {
    "Header": "XRV-SVA-V1.0", "UEnvironmentID": UE, "ScriptVenueAssociationsID": "LongerEvening-Associations", "ScriptVenueAssociationsTime": t("sva-t"),
    "REAssociations": {
        "PerformanceDesignations": {"Characters": [assoc("Anna", "Anna", "performer")],
                                    "TheatricalSetList": [{"Objects": [assoc("Lamp 1", "FX1", sink="DMX:1:2"), assoc("Lamp 2", "FX2", sink="DMX:1:3"),
                                                                       assoc("Scrim", "Scrim", sink="DMX:1:5")]}]},
        "ParticipantDesignations": {"Participants": [{"MetaName": "Audience", "VenueElementID": "Audience", "DataSourceQualifier": "hall microphone"}]},
        "OperatorDesignations": {"Consoles": [assoc("Show console", "Console", "console")]},
        "ActionElements": [{"ActionID": a, "DataSinkQualifier": "DMX:1"} for a, _, _ in actions if a.startswith("RE-")]},
    "VEAssociations": {
        "PerformanceDesignations": {"Characters": [assoc("Anna", "Bot", "performer")],
                                    "TheatricalSetList": [{"Objects": [assoc("Lamp 1", "VLamp1", sink="VLamp1"), assoc("Scrim", "Portal", sink="Portal")]}]},
        "ParticipantDesignations": {"Participants": [{"MetaName": "Guest", "VenueElementID": g} for g in ("Guest1", "Guest2", "Guest3")]},
        "ActionElements": [{"ActionID": a, "DataSinkQualifier": "MMM Property Change"} for a, _, _ in actions if a.startswith("VE-")]},
}

participant_data = {
    "Header": "XRV-PTD-V1.0", "UEnvironmentID": UE, "ParticipantDataID": "LongerEvening-PD-1", "ParticipantDataTime": t("ptd-t"), "ParticipantID": "Guest1",
    "ParticipantAttendanceHistory": [{"VisitNumber": 1, "DateOfVisit": ["2026-11-17"], "TicketClass": "Virtual", "ParticipantCategory": "Adult"}],
}


# --- the applause of the Theatre: many clappers, each a train of short noise bursts (no recording; synthesised, seeded)
def applause_pcm(seconds=6.0, rate=16000, clappers=70, seed=17):
    rnd = random.Random(seed)
    n = int(seconds * rate)
    buf = [0.0] * n
    for _ in range(clappers):
        period = 1 / rnd.uniform(3.2, 5.0)
        level = rnd.uniform(0.3, 1.0)
        tt = rnd.uniform(0, period)
        while tt < seconds:
            start = int(tt * rate)
            for k in range(int(0.012 * rate)):
                if start + k >= n: break
                env = math.exp(-k / (0.0025 * rate))
                buf[start + k] += level * env * rnd.uniform(-1, 1)
            tt += period * rnd.uniform(0.85, 1.15)
    fade = int(0.8 * rate)                      # a swell at the start, a fade at the end
    for i in range(n):
        g = min(1.0, i / (0.5 * rate)) * min(1.0, (n - i) / fade)
        buf[i] *= g
    peak = max(abs(x) for x in buf) or 1
    return [int(max(-32767, min(32767, x / peak * 0.8 * 32767))) for x in buf]


pcm = applause_pcm()
raw = struct.pack("<%dh" % len(pcm), *pcm)
os.makedirs(OUT, exist_ok=True)
with wave.open(os.path.join(OUT, "applause.wav"), "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(16000); w.writeframes(raw)

audience_audio = {
    "Header": "OSD-AUO-V1.5", "MInstanceID": MI_T, "UEnvironmentID": UE_T, "AudioObjectID": "Audience", "AudioObjectSpaceTime": {"Header": "OSD-SPT-V1.5", "SpaceTimeID": "Audience-st"},
    "BasicAudioObjectCount": 1,
    "BasicAudioObjects": [{"BAObjectIDOrBAObject": [{"Header": "OSD-BAO-V1.5", "MInstanceID": MI_T, "BasicAudioObjectID": "Audience-applause",
                                                     "BasicAudioObjectData": [{"Data": base64.b64encode(raw).decode("ascii")}],
                                                     "DescrMetadata": "16 kHz mono 16-bit PCM (little-endian), base64: the applause of the Theatre's audience (synthesised)"}]}],
}


def joints(la=0, ra=0, hip=0, knee=0):
    return {"LeftArmElevation": la, "RightArmElevation": ra, "HipFlexion": hip, "KneeFlexion": knee}


# --- THE THEATRE: time-stamped Process Actions (seconds from the start of the show)
theatre = {
    "MInstance": MI_T, "UEnvironment": UE_T, "Root": "Theatre", "Stage": "TheatreStage", "Areas": ["TheatreStage", "Hall"], "Consoles": ["Console"],
    "Venue": "TheatreVenue",
    "Locations": [{"ID": "Theatre", "X": 0, "Y": 0, "Size": 30, "Public": True},
                  {"ID": "TheatreStage", "At": "Theatre", "X": 0, "Y": 0, "Size": 10, "Public": True},
                  {"ID": "Hall", "At": "Theatre", "X": 0, "Y": -7, "Size": 10, "Public": True},
                  {"ID": "Booth", "At": "Theatre", "X": 0, "Y": -13, "Size": 3, "Public": True},
                  {"ID": "Wings", "At": "Theatre", "X": -7, "Y": 0, "Size": 3, "Public": True}],
    "Items": {"House": 1.0, "FX1": 0.0, "FX2": 0.0, "Spot": 0.0, "Scrim": 0.35, "Confetti": 0.0},
    "Processes": [{"ID": "Operator"}, {"ID": "Audience"}, {"ID": "AnnaUser", "Persona": "Anna", "Model": "catalogue/Female_Adult_01.glb"}],
    "Actions": [
        {"t": 0.5, "Process": "Operator", "Action": "MM-Add", "Nil": "Console", "At": "Booth", "Model3D": True},
        {"t": 0.6, "Process": "AnnaUser", "Action": "MM-Add", "Nil": "Anna", "At": "Wings", "X": 0.5, "Y": 1, "Yaw": -90},
        {"t": 0.8, "Process": "AnnaUser", "Action": "Animate", "Nil": "Anna", "Stream": "Anna-motion"},
        {"t": 1.0, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints()},
        {"t": 5.0, "Process": "Operator", "Action": "Property Change", "Nil": "Console", "Intensity": 1.0, "Say": "the operator presses Go"},
        {"t": 12.0, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": -4.5, "Y": 1, "Yaw": -90, "Say": "Anna walks onto the stage"},
        {"t": 13.0, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": -3.0, "Y": 1, "Yaw": -90},
        {"t": 14.0, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": -1.5, "Y": 1, "Yaw": -90},
        {"t": 15.0, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": -135},
        {"t": 15.6, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": 180},
        {"t": 26.0, "Process": "Audience", "Action": "MM-Add", "Nil": "Audience", "At": "Hall", "Audio": audience_audio, "Sound": "applause.wav", "Say": "the audience applauds"},
        {"t": 77.0, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints(60, 60)},
        {"t": 77.5, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints(120, 120)},
        {"t": 78.0, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints(170, 170), "Say": "Anna raises her arms"},
        {"t": 84.0, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints(90, 90)},
        {"t": 84.5, "Process": "AnnaUser", "Action": "Frame", "Stream": "Anna-motion", "Joints": joints()},
        {"t": 94.0, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": 135},
        {"t": 94.6, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": 90},
        {"t": 95.2, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": 45},
        {"t": 95.8, "Process": "AnnaUser", "Action": "MM-Move", "Nil": "Anna", "To": "TheatreStage", "X": 0.0, "Y": 1, "Yaw": 0, "Say": "Anna turns to face Bot on the scrim"},
    ],
}

# --- THE SQUARE
square = {
    "MInstance": MI_S, "UEnvironment": UE_S, "Root": "Square", "Stage": "SquareStage", "Areas": ["SquareStage", "Plaza"], "Consoles": [],
    "Venue": "SquareVenue",
    "Locations": [{"ID": "Square", "X": 0, "Y": 0, "Size": 30, "Public": True},
                  {"ID": "SquareStage", "At": "Square", "X": 0, "Y": 0, "Size": 8, "Public": True},
                  {"ID": "Plaza", "At": "Square", "X": 0, "Y": -6, "Size": 10, "Public": True},
                  {"ID": "Street", "At": "Square", "X": -10, "Y": 0, "Size": 3, "Public": True}],
    "Items": {"Sky": 1.0, "VLamp1": 0.0, "Portal": 0.35, "Fireflies": 0.0, "Fireworks": 0.0, "ApplauseSpeaker": 0.0},
    "Processes": [{"ID": "Guest1User", "Persona": "Guest1", "Model": "catalogue/Male_Adult_01.glb"},
                  {"ID": "Guest2User", "Persona": "Guest2", "Model": "catalogue/Female_Adult_03.glb"},
                  {"ID": "Guest3User", "Persona": "Guest3", "Model": "catalogue/Male_Adult_05.glb"},
                  {"ID": "BotService", "Persona": "Bot", "Model": "catalogue/Female_Adult_05.glb"}],
    "Reactions": [{"Process": "BotService", "Nil": "Bot", "When": {"Item": "Portal", "IntensityAtLeast": 0.9}, "After": 1.0, "Yaw": 0,
                   "Say": "Bot sees the portal brighten and turns to the Theatre"}],
    "Actions": [
        {"t": 0.5, "Process": "Guest2User", "Action": "MM-Add", "Nil": "Guest2", "At": "Plaza", "X": -1.5, "Y": 0, "Yaw": 0},
        {"t": 0.5, "Process": "Guest3User", "Action": "MM-Add", "Nil": "Guest3", "At": "Plaza", "X": 1.5, "Y": -0.5, "Yaw": 0},
        {"t": 0.6, "Process": "Guest1User", "Action": "MM-Add", "Nil": "Guest1", "At": "Street", "X": 0.5, "Y": 0, "Yaw": -90},
        {"t": 0.6, "Process": "BotService", "Action": "MM-Add", "Nil": "Bot", "At": "Street", "X": 0.5, "Y": 1.5, "Yaw": -90},
    ] + [a for g in ("Guest1", "Guest2", "Guest3") for a in (
        {"t": 0.8, "Process": g + "User", "Action": "Animate", "Nil": g, "Stream": g + "-motion"},
        {"t": 1.0, "Process": g + "User", "Action": "Frame", "Stream": g + "-motion", "Joints": joints()})] + [
        {"t": 12.5, "Process": "Guest1User", "Action": "MM-Move", "Nil": "Guest1", "To": "SquareStage", "X": -3.5, "Y": 0, "Yaw": -90, "Say": "Guest1 walks onto the stage of the Square"},
        {"t": 13.5, "Process": "Guest1User", "Action": "MM-Move", "Nil": "Guest1", "To": "SquareStage", "X": -2.5, "Y": 0, "Yaw": -90},
        {"t": 14.5, "Process": "Guest1User", "Action": "MM-Move", "Nil": "Guest1", "To": "SquareStage", "X": -1.8, "Y": 0, "Yaw": 180},
    ] + [a for g, t0 in (("Guest1", 42.0), ("Guest2", 42.5), ("Guest3", 43.0)) for a in (
        {"t": t0, "Process": g + "User", "Action": "Frame", "Stream": g + "-motion", "Joints": joints(90, 90)},
        {"t": t0 + 0.5, "Process": g + "User", "Action": "Frame", "Stream": g + "-motion", "Joints": joints(170, 170), "Say": f"{g} raises the arms"},
        {"t": t0 + 6.0, "Process": g + "User", "Action": "Frame", "Stream": g + "-motion", "Joints": joints()})] + [
        {"t": 57.0, "Process": "BotService", "Action": "MM-Move", "Nil": "Bot", "To": "SquareStage", "X": -3.5, "Y": 0.8, "Yaw": -90, "Say": "Bot walks onto the stage of the Square"},
        {"t": 58.0, "Process": "BotService", "Action": "MM-Move", "Nil": "Bot", "To": "SquareStage", "X": -1.5, "Y": 0.8, "Yaw": -90},
        {"t": 59.0, "Process": "BotService", "Action": "MM-Move", "Nil": "Bot", "To": "SquareStage", "X": 0.5, "Y": 0.8, "Yaw": -90},
        {"t": 60.0, "Process": "BotService", "Action": "MM-Move", "Nil": "Bot", "To": "SquareStage", "X": 1.5, "Y": 0.8, "Yaw": 180},
    ],
    "End": 112.0,
}
theatre["End"] = 112.0

files = {
    "Script.json": script, "ActionTable.json": action_table, "REVenueSpecification.json": re_venue, "VEVenueSpecification.json": ve_venue,
    "ScriptVenueAssociations.json": associations, "ParticipantData.json": participant_data,
    "ProcessActions-Theatre.json": theatre, "ProcessActions-Square.json": square,
}
for name, datum in files.items():
    with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
        json.dump(datum, f, indent=2, ensure_ascii=False)
        f.write("\n")
print("written", len(files), "files and applause.wav to", OUT)
