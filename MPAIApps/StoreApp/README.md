# StoreApp — Build, Deploy, Test

## What this is

`StoreApp` is the front door to the MPAI Store: the window an implementer
uses to submit an AIM Metadata (AMD) instance, have it validated against the
AMD rules, and — if valid — published into the `AMDs` folder the Controller
reads from.

This replaces the earlier console/CLI version (`StoreApp --check file.json`).
The validation and publish logic (`MpaiStore` in `AIF.Store`) is untouched;
only the front end changed, from command-line arguments to a standing GUI.

Files in this drop:

```
StoreApp.csproj    project file — now targets net10.0-windows7.0 + WinForms
Program.cs         entry point; opens the window
StoreForm.cs        the window itself
```

---

## 1. Prerequisites

- .NET 10 SDK installed on the machine you build/run on (Windows).
- The folder layout the project references expect, as siblings under one
  root (here shown as `D:\AI`; substitute your actual drive):

  ```
  D:\AI\AIF\V3.0\src\AIF.Store\AIF.Store.csproj
  D:\AI\AIMs\AMDs\...
  D:\AI\MPAIApps\StoreApp\StoreApp.csproj
  ```

  If your drive letter is `E:` instead of `D:`, that's fine — everything
  here is relative except the `StoreFolder` constant in `StoreForm.cs`
  (see step 2).

---

## 2. Before you build: check the store path

`StoreForm.cs` has one hardcoded path:

```csharp
private const string StoreFolder = @"D:\AI\AIMs\AMDs";
```

Change `D:` to whatever drive you're actually using (you've used both `D:`
and `E:` in earlier sessions — make sure this matches where `AIMs\AMDs`
really lives on the machine you're testing on). If this points at a folder
that doesn't exist, the app will still open, but the list on the left will
be empty and `Submit` will fail to publish.

---

## 3. Back up AMDs before testing

`StoreApp` writes directly into your live `AMDs` folder — the same one the
Controller reads from. Before running any of the tests below:

```powershell
Copy-Item -Recurse D:\AI\AIMs\AMDs D:\AI\AIMs\AMDs.backup
```

If anything goes wrong during testing, delete `AMDs` and rename
`AMDs.backup` back to `AMDs` to restore your known-good state.

---

## 4. Build

```powershell
cd D:\AI\MPAIApps\StoreApp
dotnet build
```

Expected: `Build succeeded`, ending with a line like:

```
StoreApp -> D:\AI\MPAIApps\StoreApp\bin\Debug\net10.0\StoreApp.dll
```

**If this fails**, paste the *full* output here — this is the first point
where a real compile error would show up, as opposed to a missing-file
error from running a `.exe` that was never built.

---

## 5. Run

```powershell
dotnet run
```

or, once built, run the exe directly from
`bin\Debug\net10.0\StoreApp.exe`.

Expected: a window titled **"MPAI Store"** opens, showing:

- a list on the left of everything currently in `AMDs`
- a **Refresh** button
- a **Submit AIM Metadata...** button
- a log area on the right (empty until you submit something)

If the list is empty, that's correct behaviour if `AMDs` is empty or the
path in step 2 is wrong — not a bug in the app.

---

## 6. Test plan

Run these in order. Each one checks a different path through
`MpaiStore.Validate` / `Publish`, so skipping ahead can hide a real bug.

### 6.1 — Reject: structurally invalid JSON

1. Copy `MMC-ASR-V2.5.json` somewhere outside `AMDs` (e.g. your Desktop).
2. Open the copy in a text editor and delete the closing `}` — break the
   JSON syntax outright.
3. In StoreApp, click **Submit AIM Metadata...** and pick the broken copy.

Expected: log shows a single `ERROR   Not valid JSON: ...` line, a
**Rejected** message box appears, and the file list on the left is
unchanged (nothing published).

### 6.2 — Reject: well-formed JSON, invalid content

1. Make another copy of an AMD (e.g. `MMC-TIQ-V2.5.json`).
2. Edit it to break one rule without breaking JSON syntax — pick one:
   - change `"AIMName": "MMC-TIQ-V2.5"` to something that doesn't match the
     `XXX-XXX-Vn.n` pattern, e.g. `"tiq"`
   - delete the entire `"Topology"` array
   - duplicate one of the entries in `"Types"` with the same `"Name"`
3. Submit it.

Expected: log shows one or more `ERROR` lines describing exactly what's
wrong (matching what you broke), **Rejected** dialog, nothing published.

### 6.3 — Publish: a clean, valid AMD

1. Temporarily move a real AMD that is *not* currently published — or
   delete one from `AMDs` first (you have the backup from step 3) — say
   `MMC-TIQ-V2.5.json`.
2. Submit the clean file.

Expected: log shows `MMC-TIQ-V2.5 is valid` is *not* shown (that message
is only for `--check`-style validate-only calls); instead you should see
`published MMC-TIQ-V2.5 -> D:\AI\AIMs\AMDs\MMC-TIQ-V2.5.json`, and it
reappears in the left-hand list immediately, without needing to click
Refresh.

You may also see `warning` lines about SubAIMs not yet in the store — that
is expected and correct if you haven't published all six MMC-AMQ SubAIMs
yet; it is not a failure.

### 6.4 — Replace flow

1. Submit the exact same file again.

Expected: a **"Already published — replace it?"** dialog appears.

- Click **No** → log shows "Publish ... was cancelled by the user", file
  on disk is unchanged.
- Submit again, click **Yes** → log shows `published ...` again, file is
  rewritten.

### 6.5 — Close the loop with the Controller

1. Make sure all of `MMC-AMQ-V2.5.json`'s SubAIMs (`CVE-VOA-V1.0`,
   `CAE-AOA-V1.0`, `MMC-ASR-V2.5`, `MMC-TIQ-V2.5`, `MMC-TTS-V2.5`,
   `CAE-AOD-V1.0`) are published via StoreApp — submit each one through
   the GUI if any are missing.
2. Run `AMQApp.exe` as usual.

Expected: `AMQApp` runs exactly as before — this confirms the Controller
is reading the same `AMDs` folder StoreApp just wrote to, i.e. the
publish-side and execution-side of the pipeline agree.

---

## 7. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `'...\StoreApp.exe' is not recognized` | The exe hasn't been built yet at that path — run `dotnet build` first, or use `dotnet run`. Build output goes to `bin\Debug\net10.0\`, not the project folder itself, unless you `dotnet publish -o`. |
| `error NETSDK1100: To build a project targeting Windows...` | You're building on a non-Windows machine without `EnableWindowsTargeting`. On real Windows this shouldn't happen. |
| List is empty on open | `StoreFolder` in `StoreForm.cs` points at a path that doesn't exist, or `AMDs` really is empty — check the constant against your actual drive letter (step 2). |
| Everything you submit gets `REJECTED` with `SubAIM ... is not in the store yet` warnings only (not errors) | That's a warning, not an error — the file *is* being published; check the list on the left, it should be there. |
| Publish succeeds but `AMQApp` doesn't see it | Confirm `StoreFolder` here and the `AmdRepository` path in `AmqAif.Host`'s `Program.cs` point at the *same* folder. |

---

## 8. Rolling back

If testing leaves `AMDs` in a bad state:

```powershell
Remove-Item -Recurse -Force D:\AI\AIMs\AMDs
Rename-Item D:\AI\AIMs\AMDs.backup AMDs
```
