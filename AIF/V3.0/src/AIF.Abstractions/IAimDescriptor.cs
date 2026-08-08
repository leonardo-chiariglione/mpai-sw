namespace AIF.Abstractions;
public interface IAimDescriptor
{
    IAimIdentifier AimIdentifier { get; }
    IImplementationIdentifier ImplementationIdentifier { get; }
    string ApiProfile { get; }
    string Description { get; }
}
