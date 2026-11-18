"""Night 2026-11-18: N-36 doing, with its note."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def status(head, new):
    global s
    i = s.index(head); j = s.index("Status: ", i); k = s.index("\n", j)
    s = s[:j] + "Status: " + new + s[k:]


def note_before_next(head, text):
    global s
    i = s.index(head); j = s.index("\n### ", i + len(head))
    s = s[:j].rstrip("\n") + "\n" + text + "\n" + s[j:]


status("### N-36 ", "doing (2026-11-18 night: built and tested up to the Module; through the MAS Service the default result does not come - see the note)")
note_before_next("### N-36 ", """NOTE 2026-11-18 02:50 (night): BUILT, NOT FINISHED. Nothing committed.
- Workflow Apps\\CUI\\CPP\\CUI-CPP.orch, one for the console and the MAS App: the avatar's explanation (what CPP does, a demonstration model trained on Polish companies, not for real decisions, what to do); every input of the Module streamed from a device "Company", every output delivered to a device "Report" (the CUI AIMs compute again whenever an input changes, so an 'ask' would read stale results: the Report keeps the latest of each). New AIF\\UserAgent\\Ua\\CompanyDevice.cs: CompanyDevice (a company given part by part, the horizon, WHAT-IF on CEA1, CEBDIFFVAL, SPPDTOT, SPPATOT, CEC3 from -50 % to +100 %), CompanyReport (the probabilities and what weighs most, as the descriptors say), CompanyReportDevice. Console CuiUa uses them (CompanyAppTests 4/4).
- App files: Apps\\CUI\\CPP\\Files\\companies (11 ready-made companies, plain names in index.json), app.json, icon.svg; the MAS Service builds the Module (Mpai.Cui.CuiProvider added to Apps\\MAS\\Service\\src\\Program.cs and its csproj); CPP added to the Apps of mas-server-AI.json (takes effect when you restart the Service).
- Clients: browser (RcaShell: company buttons, own file, horizon, what-if sliders; the report with gauges and the bars of what weighs most, beside the company chosen before) and desktop (a window "Company Performance Prediction" with the same). Both build.
- Tests: Test\\Mpai.Cui.Tests\\MasCuiAppTests.cs (4, pass): the workflow binds Company and Report; the 11 companies are valid (each part against its schema); a what-if moves only the figures asked; the Module (in process, through the codecs) gives the three results, the Report reads what the descriptors say, and twice the debts raise the default. All Mpai.Cui.Tests 36/36.
- NOT DONE: in a night copy of the MAS Service (port 5115) the browser showed the companies and gave the inputs (the Service logs number, FND, GVS, LRD...), but the default (CUI-PDD) and organisation (CUI-ORD) results never came, only the discontinuity; the same Module in process with the same AMD copy and settings gives all three. Two things found: (1) the MAS Service runs ONE shared instance of each Module for all clients (ControllerApiRunner: 'one Module, many people'); with a continuously executing Module every client's delivery loops read the same output Ports, so two clients take each other's results - a design question for the author; (2) the Service gives no remote status of a Module's AIMs (the WDL 'ask Controller for status' answers Failed), so the AIM's report could not be read. Not tried: the desktop client against the Service (the client reached the launcher only slowly), screenshots of the report. Next: find why Primary Default Prediction gives nothing inside the Service (the reports of 1CUI-PDP-V2.1-I01), then the browser/desktop runs and screenshots.""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
