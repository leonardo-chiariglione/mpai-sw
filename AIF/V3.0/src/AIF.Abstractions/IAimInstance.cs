namespace AIF.Abstractions;
public interface IAimInstance { Guid InstanceId { get; } IAimDescriptor Descriptor { get; } IAimInstance? Parent { get; } IReadOnlyCollection<IAimInstance> Children { get; } AimState State { get; } AimLocation Location { get; } }
