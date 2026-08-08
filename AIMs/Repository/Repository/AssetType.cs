namespace Mpai.Repository;

// Note: ASD here is the data type AudioSceneDescriptors, not to be confused
// with the CAE-ASD AIM (Audio Scene Delivery) - same three letters, two
// unrelated things by coincidence.
public enum AssetType
{
    BAO,
    AUO,               // AudioObject
    BAS,
    ASD,               // AudioSceneDescriptors
    AcousticProfile
}