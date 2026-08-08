using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using AIF.Store;

namespace AIF.Controller;

// Reads ExternalPort names from an AMD by DataType.
//
// This is the key piece that enables Step 4: each AIM reads its own port
// names from its own instance JSON at startup, so nothing is hardcoded.
// The AIM knows its own DataTypes; the AMD declares which port name carries
// each DataType. This class bridges the two.
public sealed class AimPortReader
{
    private readonly Dictionary<string, string> _inputPorts  = new();
    private readonly Dictionary<string, string> _outputPorts = new();

    private AimPortReader() { }

    // Load the port map for the given AIMName from the store.
    public static AimPortReader Load(AmdStore store, string aimName)
    {
        var reader   = new AimPortReader();
        var resolved = store.FindByAimName(aimName);
        if (resolved is null)
            return reader;

        var amd   = store.GetAMD(resolved).RootElement;
        if (!amd.TryGetProperty("ExternalPorts", out var ports))
            return reader;

        foreach (var port in ports.EnumerateArray())
        {
            var name      = port.GetProperty("Name").GetString()      ?? string.Empty;
            var direction = port.GetProperty("Direction").GetString()  ?? string.Empty;
            var dataType  = port.GetProperty("DataType").GetString()   ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dataType))
                continue;

            if (direction == "Input")
                reader._inputPorts[dataType]  = name;
            else if (direction == "Output")
                reader._outputPorts[dataType] = name;
        }

        return reader;
    }

    // Return the Input port name for the given DataType.
    // Throws if not found — a misconfigured AMD is a hard error.
    public string Input(string dataType)
    {
        if (_inputPorts.TryGetValue(dataType, out var name))
            return name;
        throw new InvalidOperationException(
            $"No Input port found for DataType '{dataType}'.");
    }

    // Return the Output port name for the given DataType.
    public string Output(string dataType)
    {
        if (_outputPorts.TryGetValue(dataType, out var name))
            return name;
        throw new InvalidOperationException(
            $"No Output port found for DataType '{dataType}'.");
    }

    // Return the Input port name, or a fallback if not found.
    public string InputOrDefault(string dataType, string fallback) =>
        _inputPorts.TryGetValue(dataType, out var name) ? name : fallback;

    // Return the Output port name, or a fallback if not found.
    public string OutputOrDefault(string dataType, string fallback) =>
        _outputPorts.TryGetValue(dataType, out var name) ? name : fallback;
}
