using System;
using System.Collections.Generic;
using System.IO;

// THE APPS THAT BRING THEIR OWN CONTROLLER AND THEIR OWN CLIENT (N-49, 2026-11-24). Their Module is built by a Controller program of the App (ASM, CAS, CPS, TMA), or the App is one
// program (LTP) or a server with its page (VMS); their client is a window or a program of their own, not the browser client of Thalia. Thalia starts them as their launcher of AllApps
// does - the language model first where they need one, then the Controller (here on a port Thalia chooses), then the client told where the Controller is (--controller) - and
// stops them all when the User stops the App or closes its window. The launchers of AllApps are unchanged.
//   ControllerExe    the App's Controller program, given --listen http://127.0.0.1:<port>; null: the App has none apart
//   ClientFile/Args  the App's client; {controller} is the Controller's address, {root} the folder of the software, {port} a port Thalia chose (an App that is a server),
//                    {data} the folder of this User's Thalia (where an App keeps what the User makes with it: ASM's scenes and sounds, apart from those of the launcher's ASM)
//   Page             where the User opens the App, for an App that is a server ({port}); null: the client is a window
static class OwnClients
{
    public sealed record Own(string? ControllerExe, string? ControllerProject, string ClientFile, string ClientArgs, bool LanguageModel, string? Page = null, Dictionary<string, string>? Env = null, string? ClientBuilt = null, string ControllerArgs = "");

    public static Own? Of(string id, string root)
    {
        string P(string relative) => Path.Combine(root, relative);
        string Asking(string script) => $"-NoProfile -ExecutionPolicy Bypass -File \"{P(@"AllApps\_support\" + script)}\" -Controller {{controller}}";
        return id switch
        {
            // the window of ASM (the User Agent) on the ASM Controller
            "ASM" => new(P(@"Apps\CAE\ASM\Controller\bin\Debug\net10.0\AsmController.exe"), @"Apps\CAE\ASM\Controller\AsmController.csproj",
                         P(@"Apps\CAE\ASM\Ua\bin\Debug\net10.0-windows10.0.19041.0\AsmUa.exe"), "--controller {controller} --storage \"{data}\\ASM\\module-storage\"", true,
                         ControllerArgs: "--storage \"{data}\\ASM\\module-storage\""),
            // CAS, CPS, TMA: the App's own questions (camera and microphone, or the files), as its launcher asks them, then its program on the Controller
            "CAS" => new(P(@"Apps\MMC\CAS\Controller\bin\Debug\net10.0\CasController.exe"), @"Apps\MMC\CAS\Controller\CasController.csproj",
                         "powershell", Asking("_cas.ps1"), true),
            "CPS" => new(P(@"Apps\MMC\CPS\Controller\bin\Debug\net10.0\CpsController.exe"), @"Apps\MMC\CPS\Controller\CpsController.csproj",
                         "powershell", Asking("_cps.ps1"), true),
            "TMA" => new(P(@"Apps\OSD\TMA\Controller\bin\Debug\net10.0\TmaController.exe"), @"Apps\OSD\TMA\Controller\TmaController.csproj",
                         "powershell", Asking("_tma.ps1"), false),
            // ARP, EES, EAE (2026-11-25): their workflows take files and give files through devices of their own (the tape, the selectors, the room's capture) that the browser client has
            // not; their launcher asks for the files with dialogs, as AllApps\CAE does, and runs the App on the Controller - Thalia's local one, or the MPAI as a Service instance
            "ARP" or "EES" or "EAE" => new(P(@"Thalia\Controller\bin\Debug\net10.0\ThaliaController.exe"), @"Thalia\Controller\ThaliaController.csproj",
                         "powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{P(@"AllApps\_support\_usc.ps1")}\" -App {id} -Controller {{controller}}", false,
                         ControllerArgs: $"--app {id}"),
            // the Live Theatrical Performance demonstration: one program (its User Agent and its Controller), as XRV\LTP-demo.bat runs it
            "LTP" => new(null, null, "dotnet", $"run --no-build --project \"{P(@"Apps\XRV\LTP\TwoMetaverses")}\" -- --live", false,
                         ClientBuilt: P(@"Apps\XRV\LTP\TwoMetaverses\bin\Debug\net10.0\XrvTwoMetaverses.dll")),
            // the Virtual Meeting Secretary: the ABV server of this PC, and its page
            "VMS" => new(null, null, "dotnet", $"run -c Release --project \"{P(@"Apps\ABV\AVS\Server")}\"", false, Page: "https://localhost:{page}/",
                         Env: new() { ["ABV_PORT"] = "{port}" }),
            // A-User opens the door (PGM-AUA): the MMM server of the room with its door, the Host User (the A-User App) and the Visitor, and its page; the language model of Basic Knowledge first
            "AUA" => new(null, null, "dotnet", $"run --project \"{P(@"AllApps\MMM\Server\Mpai.Mmm.Server.csproj")}\" -- http://127.0.0.1:{{port}} --door --llm llama3.2:3b", true, Page: "http://127.0.0.1:{port}/viewer/door.html?from=thalia"),
            _ => null
        };
    }
}
