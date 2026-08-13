using System.Text.Json;
using AIF.Store;

namespace AIF.Controller;

public sealed class Controller
{
    private readonly AmdStore store;

    public Controller(AmdStore store)
    {
        this.store = store;
    }

    public DescriptorGraph RegisterAim(Identifier identifier)
    {
        return new DescriptorGraph
        {
            Root = BuildNode(identifier, new HashSet<Identifier>())
        };
    }

    private DescriptorNode BuildNode(
        Identifier identifier,
        ISet<Identifier> expanding)
    {
        // Look up by AIMName only 鈥?ImplementerID and ImplementationID may be
        // placeholder strings in SubAIM references that differ from the actual
        // AMD file's Identifier. AIMName is always the stable, canonical key.
        var resolved = store.FindByAimName(identifier.AIMName);
        if (resolved is null)
        {
            return new DescriptorNode
            {
                AIMName          = identifier.AIMName,
                ImplementerID    = identifier.ImplementerID,
                ImplementationID = identifier.ImplementationID
            };
        }
        identifier = resolved;

        if (!store.Exists(identifier))
        {
            return new DescriptorNode
            {
                AIMName          = identifier.AIMName,
                ImplementerID    = identifier.ImplementerID,
                ImplementationID = identifier.ImplementationID
            };
        }

        if (!expanding.Add(identifier))
        {
            throw new InvalidOperationException(
                $"{identifier} contains itself; the AIM hierarchy is not finite.");
        }

        var root           = store.GetAMD(identifier).RootElement;
        var identifierJson = root.GetProperty("Identifier");

        var node = new DescriptorNode
        {
            AIMName          = identifierJson.GetProperty("AIMName").GetString()          ?? string.Empty,
            ImplementerID    = identifierJson.GetProperty("ImplementerID").GetString()    ?? string.Empty,
            ImplementationID = identifierJson.GetProperty("ImplementationID").GetString() ?? string.Empty
        };

        // ExternalPorts
        if (root.TryGetProperty("ExternalPorts", out var externalPorts))
        {
            foreach (var port in externalPorts.EnumerateArray())
            {
                node.Ports.Add(new RuntimePort
                {
                    Name      = port.GetProperty("Name").GetString()      ?? string.Empty,
                    Direction = port.GetProperty("Direction").GetString()  ?? string.Empty,
                    DataType  = port.GetProperty("DataType").GetString()   ?? string.Empty,
                    Technology= port.GetProperty("Technology").GetString() ?? string.Empty,
                    Protocol  = port.GetProperty("Protocol").GetString()   ?? string.Empty,
                    IsRemote  = port.GetProperty("IsRemote").GetBoolean()
                });
            }
        }

        // InternalTypes  (InternalType name -> DataType)
        if (root.TryGetProperty("InternalTypes", out var internalTypes))
        {
            foreach (var it in internalTypes.EnumerateArray())
            {
                var name     = it.GetProperty("Name").GetString()     ?? string.Empty;
                var dataType = it.GetProperty("DataType").GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dataType))
                    node.InternalTypes[name] = dataType;
            }
        }

        // SubAIMs
        if (root.TryGetProperty("SubAIMs", out var subAims))
        {
            foreach (var subAim in subAims.EnumerateArray())
            {
                var subId = new Identifier
                {
                    AIMName          = subAim.GetProperty("Identifier").GetProperty("AIMName").GetString()          ?? string.Empty,
                    ImplementerID    = subAim.GetProperty("Identifier").GetProperty("ImplementerID").GetString()    ?? string.Empty,
                    ImplementationID = subAim.GetProperty("Identifier").GetProperty("ImplementationID").GetString() ?? string.Empty
                };
                if (!string.IsNullOrWhiteSpace(subId.AIMName))
                    node.Children.Add(BuildNode(subId, expanding));
            }
        }

        // Topology
        // Convention in the instance JSON:
        //   "Input"  = the PRODUCING side (data leaves that AIM 鈥?its port is InputOutput)
        //   "Output" = the RECEIVING side (data enters that AIM 鈥?its port is OutputInput)
        if (root.TryGetProperty("Topology", out var topology))
        {
            foreach (var connection in topology.EnumerateArray())
            {
                var producer = connection.GetProperty("Input");   // producing AIM
                var consumer = connection.GetProperty("Output");  // receiving AIM

                node.Connections.Add(new TopologyConnection
                {
                    Source      = Endpoint(producer),  // who produces
                    Destination = Endpoint(consumer)   // who consumes
                });
            }
        }

        expanding.Remove(identifier);
        return node;
    }

    private static string Endpoint(JsonElement port)
    {
        var aimName  = port.GetProperty("AIMName").GetString()  ?? string.Empty;
        var portName = port.GetProperty("PortName").GetString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(aimName)
            ? portName
            : $"{aimName}.{portName}";
    }

    public IReadOnlyList<string> Instantiate(
        DescriptorGraph graph,
        IAimProvider provider,
        AimSettings settings,
        AimHost host)
    {
        var instantiated = new List<string>();
        InstantiateNode(graph.Root, provider, settings, host, instantiated);
        return instantiated;
    }

    private void InstantiateNode(
        DescriptorNode node,
        IAimProvider provider,
        AimSettings settings,
        AimHost host,
        List<string> instantiated)
    {
        foreach (var child in node.Children)
        {
            if (child.IsComposite)
            {
                InstantiateNode(child, provider, settings, host, instantiated);
                continue;
            }

            var aimName = child.AIMName;
            if (string.IsNullOrWhiteSpace(aimName) || instantiated.Contains(aimName))
                continue;

            CheckResources(child);
            host.RegisterRuntime(provider.Create(aimName, settings.For(aimName)));
            instantiated.Add(aimName);
        }
    }

    private void CheckResources(DescriptorNode node)
    {
        var identifier = new Identifier
        {
            AIMName          = node.AIMName,
            ImplementerID    = node.ImplementerID,
            ImplementationID = node.ImplementationID
        };

        if (!store.Exists(identifier)) return;

        var policies = ResourcePolicy.ReadFrom(store.GetAMD(identifier).RootElement);
        foreach (var policy in policies)
        {
            if (policy.Name != "Memory") continue;
            var minimum   = ResourcePolicy.MemoryBytes(policy.Minimum);
            if (minimum is null) continue;
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (available > 0 && available < minimum)
                Console.WriteLine(
                    $"[AIF] {node.AIMName} requests at least {policy.Minimum}; " +
                    $"machine reports {available / (1024.0 * 1024 * 1024):0.0}_GB.");
        }
    }
}
