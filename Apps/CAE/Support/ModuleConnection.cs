using System;

using Mpai.Aif.Api;

namespace Mpai.Cae.Apps;

// THE CONTROLLER THE APP USES (N-49, 2026-11-24). Without --controller the App starts the Module on a Controller of its own (ModuleHost), as before. With --controller <url> it runs the same
// User Agent on the Controller at that address, through the Controller API: the local Controller Thalia started for it, or an MPAI as a Service instance. The Module is then that Controller's
// business - which AIMs, which settings (a profile such as "openvoice" is not sent: it is the deployment's).
public sealed class ModuleConnection : IDisposable
{
    private readonly ModuleHost? _host;
    private readonly Mpai.Mas.Client.RemoteControllerApi? _remote;
    public IControllerApi Api { get; }

    private ModuleConnection(ModuleHost? host, Mpai.Mas.Client.RemoteControllerApi? remote) { _host = host; _remote = remote; Api = (IControllerApi?)host?.Api ?? remote!; }

    public static ModuleConnection Open(string module, string? controller, string? profile = null, Action<string>? say = null)
    {
        if (string.IsNullOrWhiteSpace(controller)) return new ModuleConnection(ModuleHost.Start(module, profile, say), null);
        say?.Invoke($"The Module {module} on the Controller at {controller}.");
        return new ModuleConnection(null, new Mpai.Mas.Client.RemoteControllerApi(controller));
    }

    // The value of --controller in the arguments, and the arguments without it (for the Apps that read their options themselves).
    public static (string? Controller, string[] Others) Take(string[] args)
    {
        var at = Array.IndexOf(args, "--controller");
        if (at < 0) return (null, args);
        if (at + 1 >= args.Length) throw new ArgumentException("--controller takes the address of a Controller.");
        var rest = new string[args.Length - 2];
        Array.Copy(args, 0, rest, 0, at); Array.Copy(args, at + 2, rest, at, args.Length - at - 2);
        return (args[at + 1], rest);
    }

    public void Dispose() { _host?.Dispose(); _remote?.Dispose(); }
}
