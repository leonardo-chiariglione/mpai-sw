# Thalia

Thalia is the environment, designed for MPAI-AIF and MPAI-MAS, in which a User finds, obtains, runs and stops MPAI Apps (MPAI-THL; M3323). This is its reference implementation. `Thalia.bat` builds what changed and opens it

`Thalia-demo.bat` is Thalia with its three places to look for an App all answering: the Apps on this machine, the Apps in the MPAI Store and the Apps of an MPAI as a Service instance. It starts the instance of this machine (it takes about a minute to load its models) and Thalia with a Store and data of its own (`%LOCALAPPDATA%\Thalia-demo`), whose shelf is given every App that comes with the software; your own Thalia, Store and Apps are not touched. Thalia comes up on Home, which shows the functions a User can perform; Find an App searches the three places at once.
full screen; "Return to Windows" brings the desktop back.

## What it does today

| Function | What you can do | Requirements of M3323 |
|---|---|---|
| **Find an App** | Search the Apps of this machine and of MPAI as a Service, with what each App's Module needs (AIMs, resources, whether this machine can run it) | APP-2, APP-3 |
| **Run, pause, resume, stop** | Run an App here or on MPAI as a Service in the same way. A local App can be paused (every AIM of its Module completes what it is doing and waits) and resumed | APP-6, GEN-3 |
| **Obtain an App** | The **Store** view lists the Apps of the MPAI Store. For each, Thalia shows what its Module needs, the AIMs it uses, **what each AIM obtains and under which licence** (a non-commercial licence or personal data is said before you accept), and whether this machine can run it. Nothing is kept until you accept. Thalia obtains the App; the Controller obtains the Module | APP-4, APP-5 |
| **Update and remove** | The Store says when an App has a newer version; Update replaces the version held. Remove removes the App only: its Module stays with the Controller, because another App may use it. Apps that come with Thalia are not removed | APP-8 |
| **Devices** | Choose the microphone, the camera and the screen, see whether each works (a microphone level, a camera picture, a window on that screen) and give the choice to every App Thalia starts | APP-7 |
| **Settings** | The address of the MPAI as a Service instance and of the Store, each tested before it is kept; where Thalia looks by default; where your things are kept | APP-1 |

Not done yet, and shown as "coming": **Make your own Module** (the AIM life cycle, AIM-1 to AIM-9: its open points are in the discussion of M3323) and **Talk to Thalia**. Remote placement (APP-9), consents (GEN-4) and attestation
(GEN-5) are not in this version.

## How it is made

- `Core` is Thalia: its screen (`wwwroot/launcher.html`), the catalogue, the Store side (`Store.cs`), starting and stopping Apps. It reaches a Controller only through the Controller-User Agent API (GEN-1): to start a local
  App's Controller, and to ask it to pause and resume (`MasServer.ExclusiveControl`: a Controller that serves one User acts on the Module; a shared MPAI as a Service instance still only changes a label).
- `Controller` is the local Controller of one running App.
- `Store\Service` (the MPAI Store) keeps the Apps as packages (`AppShelf.cs`): app.json, the Workflow, the icon. A version is never overwritten. `Store\Service\Submit-Apps.ps1` publishes Apps.
- What the User obtains is in `%LOCALAPPDATA%\Thalia` (`Apps`, `installed.json`, `Store`, `preferences.json`); `--Data` names another folder.
- The Store runs on this machine when it is needed (port 5020); Thalia gives its Store the Apps that come with it, once.

## Tests

`powershell -ExecutionPolicy Bypass -File Thalia\Test-Thalia.ps1` starts its own Thalia and Store in a temporary folder and checks 28 things without a person (the Store, obtain, update, remove, run, pause, resume, settings);
`-NoRun` leaves out running an App (it takes a minute or two). `dotnet test Test\Mpai.Aif.Tests --filter ThaliaStoreTests` tests the Apps shelf of the Store.
