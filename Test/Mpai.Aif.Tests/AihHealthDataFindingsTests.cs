using System.Text.Json.Nodes;

namespace Mpai.Aif.Tests;

// WHAT HEALTH DATA SHOW, AND HOW SURE ONE IS OF IT (2026-11-15). The author: the label of a datum (what it shows) goes in the specialised Qualifier of its
// domain, coded in a vocabulary of that domain; how sure the source is goes in the AIH Data. The Health Data Qualifiers (TFA V1.5, draft) state ONE
// Sub-Type and ONE Format (the five written as catalogues were corrected), and each has Findings: FindingID, Code, CodingSystem (the domain's), LabelSet,
// Where. AIH Data has FindingTrust: for each Finding, its Source and Confidence. The case is N-22 of the night list: a chest X-ray of NIH ChestX-ray14.
[Trait("Group", "Fast")]
public class AihHealthDataFindingsTests
{
    private static JsonObject Time() => new() { ["Header"] = "OSD-TIM-V1.5", ["TimeID"] = "t", ["Data"] = "2026-11-15T12:00:00Z" };

    private static JsonObject ChestXRay(string codingSystem = "DatasetLabelSet", JsonNode? subTypes = null, double confidence = 0.9) => new()
    {
        ["Header"] = "AIH-AHD-V1.1", ["UserID"] = "end-user-2", ["AIHDataID"] = "aih-00000013_005",
        ["AIHData"] = new JsonObject
        {
            ["HealthData"] = new JsonObject
            {
                ["Header"] = "AIH-HLD-V1.1", ["HealthDataID"] = "00000013_005.png", ["HealthDataTime"] = Time(),
                ["HealthDataData"] = new JsonArray(new JsonObject { ["Data"] = "iVBORw0KGgo=" }),
                ["DataQualifier"] = new JsonObject
                {
                    ["Header"] = "TFA-HDQ-V1.5", ["HealthDataQualifierID"] = "hdq-1",
                    ["SpecialisedQualifier"] = new JsonObject
                    {
                        ["Header"] = "TFA-MIQ-V1.5", ["MedicalImagingQualID"] = "miq-1",
                        ["SubTypes"] = subTypes ?? new JsonObject { ["SubTypeGroup"] = "X-Ray", ["SubType"] = "Projection Radiography" },
                        ["Formats"] = new JsonObject { ["FormatGroup"] = "ImageExchangeFormats", ["Format"] = "PNG" },
                        ["Attributes"] = new JsonObject { ["AnatomicalRegion"] = "Chest", ["Modality"] = "DX", ["Orientation"] = "PA" },
                        ["Findings"] = new JsonArray(new JsonObject
                        {
                            ["FindingID"] = "f1", ["Code"] = "Effusion", ["CodingSystem"] = codingSystem, ["LabelSet"] = "NIH ChestX-ray14",
                            ["Where"] = new JsonObject { ["X"] = 610, ["Y"] = 540, ["Width"] = 300, ["Height"] = 260 }
                        })
                    }
                }
            },
            ["FindingTrust"] = new JsonArray(new JsonObject { ["FindingID"] = "f1", ["Source"] = "Report", ["SourceID"] = "NIH NLP labeller", ["Confidence"] = confidence }),
            ["ModelLicence"] = "licence-1"
        }
    };

    private static string Check(JsonObject o) => PgmRig.Check(o);

    [Fact]
    public void AChestXRayWithItsFindingAndTheTrustInItIsValidAihData() => Assert.Equal("valid", Check(ChestXRay()));

    [Fact]
    public void AFindingOfAnXRayCannotBeCodedInAVocabularyOfECGs() => Assert.NotEqual("valid", Check(ChestXRay(codingSystem: "SCP-ECG")));

    [Fact]
    public void AnImageHasOneSubTypeNotAListOfAll() =>
        Assert.NotEqual("valid", Check(ChestXRay(subTypes: new JsonObject { ["X-Ray"] = new JsonArray("Projection Radiography"), ["ComputedTomography"] = new JsonArray("Conventional CT") })));

    [Fact]
    public void AConfidenceIsBetweenZeroAndOne() => Assert.NotEqual("valid", Check(ChestXRay(confidence: 1.4)));

    // An ECG of the same End User, with a finding in the vocabulary of ECGs: the same scheme for every domain.
    [Fact]
    public void AnEcgFindingIsCodedInAVocabularyOfEcgs()
    {
        var ecg = ChestXRay();
        var q = ecg["AIHData"]!["HealthData"]!["DataQualifier"]!.AsObject();
        q["SpecialisedQualifier"] = new JsonObject
        {
            ["Header"] = "TFA-ECQ-V1.5", ["ECGQualifierID"] = "ecg-1",
            ["SubTypes"] = new JsonObject { ["LeadConfiguration"] = "12-lead" }, ["Formats"] = new JsonObject(),
            ["Findings"] = new JsonArray(new JsonObject { ["FindingID"] = "f1", ["Code"] = "AFIB", ["CodingSystem"] = "SCP-ECG", ["Where"] = new JsonObject { ["Start"] = 0.0, ["End"] = 10.0, ["Leads"] = new JsonArray("II", "V1") } })
        };
        Assert.Equal("valid", Check(ecg));
    }
}
