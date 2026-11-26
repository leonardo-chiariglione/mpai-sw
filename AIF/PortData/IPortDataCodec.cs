using System;
using System.Collections.Generic;

namespace Mpai.Aif.PortData;

// MAS "port-data" codec for ONE Data Type.
//
// TWO SERIALISATIONS, NOT ONE. Inside a process an Object is whatever this
// implementation's C# happens to serialise to. On the wire it must be the
// instance its MPAI schema describes: the Header is the constant the schema
// pins, the names are the schema's names, and the data sits in the array form
// the schema defines. Another implementer's AIMs will serialise differently
// inside and remain conformant outside; that is the whole point of the
// boundary, and the reason a codec exists rather than a pass-through.
//
// ONE CODEC PER DATA TYPE, AND NO APPLICATION ANYWHERE. A codec knows a Data
// Type. It does not know which Module is running, which application asked, or
// what any port is called. Adding a Data Type means adding a codec; it never means
// editing the server that three applications share.
public interface IPortDataCodec
{
    // The Data Type this codec serves, e.g. "OSD-BSO-V1.5". This is the value
    // the schema pins as the Object's Header, and the value the MAS route
    // carries. Never a port name.
    string DataType { get; }

    // MAS port-data bytes -> the internal object JSON the Controller and AIMs
    // read (MpaiJson).
    string ToInternal(byte[] wire);

    // Internal object JSON -> MAS port-data bytes conforming to the schema.
    byte[] ToWire(string internalJson);
}

// The set of Data Types a server or client can carry.
//
// A LOOKUP, NOT A SWITCH. The old MAS server chose its conversion with a switch
// over port NAMES, so it carried cases for ports belonging to Modules it was not
// running, and any type without a case fell through to "send whatever the client
// chose" - which made one client's private serialisation the de facto protocol.
// Here an unregistered Data Type is an error the caller can report, not a silent
// pass-through.
public sealed class PortDataCodecs
{
    private readonly Dictionary<string, IPortDataCodec> byDataType =
        new(StringComparer.Ordinal);

    // The codecs both ends need to speak the types in use today. A new Data Type
    // is added here and in a file of its own; nothing else changes.
    public static PortDataCodecs Default() =>
        new PortDataCodecs()
            .Register(new BasicSpeechObjectCodec())
            .Register(new BasicTextObjectCodec())
            .Register(new BasicVisualObjectCodec())
            .Register(new FaceDescriptorsObjectCodec())
            .Register(new SimpleTimeCodec())
            .Register(new SelectorCodec())
            .Register(new SummaryCodec())
            .Register(new EntityPersonalStatusCodec())
            .Register(new Basic3DModelObjectCodec())
            .Register(new PortableAvatarCodec())
            .Register(new PointCodec())
            // Audio Scene Management: the same JSON instance inside and on the wire.
            .Register(new JsonObjectCodec("OSD-BAO-V1.5"))
            .Register(new JsonObjectCodec("OSD-BAS-V1.5"))
            .Register(new JsonObjectCodec("OSD-BSS-V1.5"))
            .Register(new JsonObjectCodec("OSD-BMS-V1.5"))
            .Register(new JsonObjectCodec("OSD-AUO-V1.5"))
            // Human-CAV Interaction (N-50 l, 2026-11-25): the LiDAR frame of the cabin, the Offline Map, the user's Instance Identifier and the CAV messages, so that the HCI Module can
            // run on a Controller reached through the Controller API (the MAS instance; until then the LiDAR frame stopped the turn)
            .Register(new JsonObjectCodec("OSD-BLO-V1.5"))
            .Register(new JsonObjectCodec("OSD-BOO-V1.5"))
            .Register(new JsonObjectCodec("OSD-IID-V1.5"))
            .Register(new JsonObjectCodec("CAV-AHM-V2.0"))
            .Register(new JsonObjectCodec("CAV-ERH-V2.0"))
            // the Audio Scene Descriptors and the other scenes the Module of ASM gives (ASM-VCI.orch)
            .Register(new JsonObjectCodec("OSD-ASD-V1.5"))
            .Register(new JsonObjectCodec("OSD-SPO-V1.5"))
            .Register(new JsonObjectCodec("OSD-SSD-V1.5"))
            .Register(new JsonObjectCodec("OSD-MSD-V1.5"))
            .Register(new JsonObjectCodec("OSD-OPV-V1.5"))
            .Register(new JsonObjectCodec("CAE-UCM-V1.0"))
            // the Emotion an App gives a Module (CAE-USC Add Emotion to Speech): the JSON instance of its schema, inside and on the wire
            .Register(new JsonObjectCodec("MMC-EEM-V2.5"))
            .Register(new JsonObjectCodec("CAE-DLS-V2.5"))      // the Damaged List of the Speech Restoration System
            // Television Media Analysis (OSD-TMA): the auxiliary text of a programme and the Multimodal Event Descriptors, the JSON instance of their schema, inside and on the wire.
            .Register(new JsonObjectCodec("OSD-TXO-V1.5"))
            .Register(new JsonObjectCodec("OSD-MEV-V1.5"))
            // Company Performance Prediction (MPAI-CUI CPP V2.1): the descriptors and statements are written and read as the JSON instance of their schema, inside and on the wire.
            .Register(new JsonObjectCodec("CUI-FND-V2.1"))
            .Register(new JsonObjectCodec("CUI-GVS-V2.1"))
            .Register(new JsonObjectCodec("CUI-LRD-V2.1"))
            .Register(new JsonObjectCodec("CUI-CRD-V2.1"))
            .Register(new JsonObjectCodec("CUI-SRS-V2.1"))
            .Register(new JsonObjectCodec("CUI-ORD-V2.1"))
            .Register(new JsonObjectCodec("CUI-PDD-V2.1"))
            .Register(new JsonObjectCodec("CUI-PSD-V2.1"))
            .Register(new JsonObjectCodec("CUI-SRM-V2.1"))
            // Health Data (MPAI-AIH HSP V1.1): the Audit and the AIH Data and the Licence Confirm of the Auditing AIM, the JSON instance of their schema, inside and on the wire (N-45).
            .Register(new JsonObjectCodec("AIH-ADT-V1.1"))
            .Register(new JsonObjectCodec("AIH-AHD-V1.1"))
            .Register(new JsonObjectCodec("AIH-LCF-V1.1"))
            // A-User of PGM-AUA (2026-11-26): the Human Command, the Action and the Process Action of the A-User Module, so that the door App can run it on a Controller reached
            // through the Controller API (the MAS instance)
            .Register(new JsonObjectCodec("PGM-HCM-V1.0"))
            .Register(new JsonObjectCodec("MMM-ACT-V2.2"))
            .Register(new JsonObjectCodec("MMM-PAC-V2.2"))
            // Live Theatrical Performance (XRV-LTP, 2026-11-26): the show's documents and the data of the two venues, so that the LTP App can run its Module on the MAS instance
            .Register(new JsonObjectCodec("XRV-SCR-V1.0"))
            .Register(new JsonObjectCodec("XRV-ATB-V1.0"))
            .Register(new JsonObjectCodec("XRV-SVA-V1.0"))
            .Register(new JsonObjectCodec("XRV-RVS-V1.0"))
            .Register(new JsonObjectCodec("XRV-VVS-V1.0"))
            .Register(new JsonObjectCodec("XRV-PTD-V1.0"))
            .Register(new JsonObjectCodec("XRV-RDI-V1.0"))
            .Register(new JsonObjectCodec("XRV-RDO-V1.0"))
            .Register(new JsonObjectCodec("XRV-VDI-V1.0"))
            .Register(new JsonObjectCodec("XRV-VDO-V1.0"))
            .Register(new NumberCodec())
            .Register(new BooleanCodec())
            .Register(new StringCodec());

    public PortDataCodecs Register(
        IPortDataCodec codec)
    {
        byDataType[codec.DataType] = codec;
        return this;
    }

    public bool Knows(
        string dataType) =>
        byDataType.ContainsKey(dataType);

    public IReadOnlyCollection<string> KnownDataTypes => byDataType.Keys;

    // Throws rather than guessing. A caller at an HTTP boundary should turn this
    // into 415 Unsupported Media Type: the peer asked for a Data Type this build
    // cannot carry, and saying so is more useful than delivering bytes that
    // happen to be shaped like something else.
    // EVERY CROSSING IS CHECKED AGAINST THE PUBLISHED SCHEMA. The serialisers
    // convert between the internal representation and the schema instance, and
    // nothing verified the second half of that claim until now. Checked here
    // rather than at each call site so that the server, the client and the
    // round-trip test are all covered by one place.
    public byte[] ToWire(string dataType, string internalJson)
    {
        var wire = For(dataType).ToWire(internalJson);
        PortDataSchema.Check(dataType, "produced", wire);
        return wire;
    }

    public string ToInternal(string dataType, byte[] wire)
    {
        PortDataSchema.Check(dataType, "received", wire);
        return For(dataType).ToInternal(wire);
    }

    public IPortDataCodec For(
        string dataType) =>
        byDataType.TryGetValue(dataType, out var codec)
            ? codec
            : throw new NotSupportedException(
                  $"No port-data codec for Data Type '{dataType}'. " +
                  $"Known: {string.Join(", ", byDataType.Keys)}.");
}