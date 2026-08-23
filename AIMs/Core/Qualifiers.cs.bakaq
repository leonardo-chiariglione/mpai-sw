namespace Mpai.Core;

// ---------------------------------------------------------------------------
//  Qualifier types, projecting the TFA/V1.5 Qualifier schemas. A Qualifier is
//  the running description of an object: it is inherited in part from the
//  input and determined in part by the producing AIM.
// ---------------------------------------------------------------------------

// TFA/V1.5/data/TextQualifier.json
public sealed class TextQualifier
{
    public string Header { get; init; } = "TFA-TXQ-V1.5";
    public string? MInstanceID { get; init; }
    public string? UEnvironmentID { get; init; }
    public string TextQualifierID { get; init; } = "";
    public SpaceTime? TextQualifierTime { get; init; }

    public SubType? SubType { get; init; }
    public TextFormat? Format { get; init; }
    public TextAttributes? Attributes { get; init; }

    public DataExchangeMetadata? DataXMData { get; init; }
    public string? DescrMetadata { get; init; }
}

public sealed class TextFormat
{
    public TextContentFormat? ContentFormat { get; init; }
}

public sealed class TextContentFormat
{
    public string? Static { get; init; }    // TextStaticFormat.*
    public object? Dynamic { get; init; }    // TFA TextDynamicFormats.json (not yet provided)
}

public sealed class TextAttributes
{
    public InstanceIdentifier? ObjectIdentifier { get; init; }
    public Language? Language { get; init; }
}

// Shared by TextQualifier and SpeechQualifier (same shape in both schemas).
public sealed class Language
{
    public string? LanguageCode { get; init; }
    public string? LanguageFormat { get; init; }   // LanguageFormat.*
}

// SubType is an open object in the schemas; kept as an extensible placeholder.
public sealed class SubType { }

// TFA/V1.5/data/AudioQualifier.json â€” schema not yet provided. Reuses the
// same Format/Attributes shape as SpeechQualifier (PCM, file format, and
// device metadata aren't inherently speech-specific) rather than an empty
// placeholder that would discard real backend-determined data. Revisit the
// internals here once the real schema arrives; BasicAudioObject's use of
// this type does not need to change.
public sealed class AudioQualifier
{
    public string Header { get; init; } = "TFA-AUQ-V1.5";     // placeholder header
    public string? MInstanceID { get; init; }
    public string? UEnvironmentID { get; init; }
    public string AudioQualifierID { get; init; } = "";
    public SpaceTime? AudioQualifierTime { get; init; }

    public SubType? SubType { get; init; }
    public SpeechFormat? Format { get; init; }
    public SpeechAttributes? Attributes { get; init; }

    public DataExchangeMetadata? DataXMData { get; init; }
    public string? DescrMetadata { get; init; }
}

// TFA/V1.5/data/SpeechQualifier.json
public sealed class SpeechQualifier
{
    public string Header { get; init; } = "TFA-SPQ-V1.5";
    public string? MInstanceID { get; init; }
    public string? UEnvironmentID { get; init; }
    public string SpeechQualifierID { get; init; } = "";
    public SpaceTime? SpeechQualifierTime { get; init; }

    public SubType? SubType { get; init; }
    public SpeechFormat? Format { get; init; }
    public SpeechAttributes? Attributes { get; init; }

    public DataExchangeMetadata? DataXMData { get; init; }
    public string? DescrMetadata { get; init; }
}

public sealed class SpeechFormat
{
    public SpeechContentFormats? ContentFormats { get; init; }
    public SpeechTransportFormats? TransportFormats { get; init; }
}

public sealed class SpeechContentFormats
{
    public Pcm? RawData { get; init; }
    public object? OtherContentFormats { get; init; }   // TFA SpeechContentFormats.json (not yet provided)
}

public sealed class SpeechTransportFormats
{
    public string? FileFormat { get; init; }    // SpeechFileFormat.*
    public object? StreamFormat { get; init; }   // TFA SpeechStreamFormats.json (not yet provided)
}

public sealed class SpeechAttributes
{
    public string? Source { get; init; }         // SpeechSource.*  (Real | Synthetic)
    public SpeechMetadata? Metadata { get; init; }
    public SpeechCharacteristics? SpeechCharacteristics { get; init; }
    public SpeechStructure? Structure { get; init; }
    public AudioDevice? Device { get; init; }
}

public sealed class SpeechMetadata
{
    public Language? Language { get; init; }
    public InstanceIdentifier? SpeakerIdentity { get; init; }
    public SpeakerProperties? SpeakerProperties { get; init; }
    public ContentDescription? ContentDescription { get; init; }
}

public sealed class SpeakerProperties
{
    public string? SpeakerType { get; init; }    // SpeakerType.*  (Human | Agent | Unknown)
    public int? SpeakerCount { get; init; }
}

public sealed class ContentDescription
{
    public TextObject? TextObject { get; init; }          // OSD TextObject (the spoken text, for provenance)
    public PersonalStatus? EntityInternalStatus { get; init; }
}

// Following blocks are populated by the Speech-Characteristics / Capture / Render
// AIMs (later slices); projected here to keep SpeechQualifier faithful.
public sealed class SpeechCharacteristics
{
    public ValueUnit? SpeakingRate { get; init; }   // Unit: WordsPerSecond | SyllablesPerSecond
    public ValueUnit? PitchRange { get; init; }     // Unit: Hertz | Semitones
    public ValueType? Energy { get; init; }         // Type: RMS | Peak | LUFS
    public string? Prosody { get; init; }           // Neutral | Expressive | Emphatic | Monotonic | Other
    public bool? Disfluencies { get; init; }
}

public sealed class ValueUnit { public double? Value { get; init; } public string? Unit { get; init; } }
public sealed class ValueType { public double? Value { get; init; } public string? Type { get; init; } }

public sealed class SpeechStructure
{
    public int? UtteranceCount { get; init; }
    public bool? TurnBased { get; init; }
}

public sealed class AudioDevice
{
    public string? DeviceID { get; init; }
    public string? DeviceRole { get; init; }    // Capture | Render | Bidirectional
    public string? DeviceType { get; init; }    // Microphone | Speaker | ...
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public CaptureConfiguration? CaptureConfiguration { get; init; }
    public RenderConfiguration? RenderConfiguration { get; init; }
    public Synchronisation? Synchronisation { get; init; }
    // DeviceLocation / DeviceGeometry / MicrophoneDirectivityFormat refs: not yet provided.
}

public sealed class CaptureConfiguration { public int? ChannelCount { get; init; } public string? SamplingMode { get; init; } }
public sealed class RenderConfiguration  { public int? ChannelCount { get; init; } public string? RenderingMode { get; init; } }
public sealed class Synchronisation      { public string? ClockType { get; init; } public string? Reference { get; init; } }