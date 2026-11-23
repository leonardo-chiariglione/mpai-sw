using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using AIF.Controller;

namespace Mpai.Thalia.Controller;

// THE CONTROLLER'S OWN GLUE for the AIMs of the Store this Controller knows how to build from a package that carries nothing but the Implementation (N-45). The package stays what its
// Implementer published - the assembly named by BinaryName in the AIM Metadata and its .deps.json; the Controller finds the processor in it and builds the AIM.
// The libraries such a package needs and does not carry (Mpai.Aih.Common, Mpai.Core) are the Controller's: it has them.
internal static class PackageAdapters
{
    public static IReadOnlyList<IPackageAdapter> All { get; } = [new AihAdt()];

    // AIH-ADT-V1.1, Auditing: Mpai.Aih.Adt.AdtAimProcessor(name, ports, BackEndId).
    private sealed class AihAdt : IPackageAdapter
    {
        public string AimName => "AIH-ADT-V1.1";

        public IAimProcessor? Create(Assembly implementation, AimPortReader ports, IReadOnlyDictionary<string, string> settings) =>
            implementation.GetType("Mpai.Aih.Adt.AdtAimProcessor") is { } type
                ? (IAimProcessor?)Activator.CreateInstance(type, AimName, ports, settings.TryGetValue("BackEndId", out var backEnd) ? backEnd : "")
                : null;
    }
}
