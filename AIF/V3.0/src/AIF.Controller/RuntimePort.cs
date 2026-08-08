namespace AIF.Controller;

// Runtime representation of an AIM ExternalPort.
public sealed class RuntimePort
{
    public string Name { get; init; } =
        string.Empty;

    public string Direction { get; init; } =
        string.Empty;

    public string DataType { get; init; } =
        string.Empty;

    public string Technology { get; init; } =
        string.Empty;

    public string Protocol { get; init; } =
        string.Empty;

    public bool IsRemote { get; init; }
}