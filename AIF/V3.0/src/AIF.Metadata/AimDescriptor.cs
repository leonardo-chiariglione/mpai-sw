namespace AIF.Metadata;
public sealed class AimDescriptor
{
    public AimIdentifier AimIdentifier { get; set; } = new();
    public ImplementationIdentifier ImplementationIdentifier { get; set; } = new();
    public string ApiProfile { get; set; } = "Basic";
    public string Description { get; set; } = string.Empty;
}
