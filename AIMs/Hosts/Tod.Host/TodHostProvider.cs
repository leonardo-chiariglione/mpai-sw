using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Osd.Tod;   // TodAimProcessor, ConsoleModelDelivery

namespace Tod.Host;

// Composition root for the 3OD test host: provides 3D Model Object Delivery with a
// headless (console) device that reports what it would render. The graphical
// WebView-backed renderer is provided by the CAV application that owns a display.
internal sealed class TodHostProvider : IAimProvider
{
    private readonly AmdStore _store;
    public TodHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "OSD-3OD-V1.5" =>
                new TodAimProcessor(aimName, new ConsoleModelDelivery(), AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"TodHostProvider does not provide '{aimName}'.")
        };
}
