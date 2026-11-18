"""Night 2026-11-18: N-36 done (a second note below the first)."""
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


status("### N-36 ", "done (2026-11-18 night: the CUI App works in the MAS App, browser and desktop; two questions in the notes)")
note_before_next("### N-36 ", """NOTE 2026-11-18 02:50 (night, second): DONE. The cause of the missing results was in the browser's remote Controller API (AIF\\UserAgent\\Remote\\RemoteControllerApiAsync.cs): it did not tell the Service the read timeout (?timeout=), as the synchronous client does, so the Service held each of the Report's four reads 60 s and the browser (six connections to a host) had none left to write the inputs; fixed. Also: the discontinuity bars scaled as percentages; the company buttons wrap.
- Tried in a night copy of the MAS Service (port 5115): browser - the companies, the healthy company and the company in distress side by side (default 2.9 % / 16.7 %, organisation +9.6 % / +38.3 %, discontinuity 11.1 % / 34.1 %), what-if twice the debts of the company in distress: default 23.4 %, DebtOnSales weighs most (35 %); screenshots C:\\Users\\Thor\\Downloads\\cui-app-1-companies.png, cui-app-2-healthy-vs-distressed.png, cui-app-3-what-if-twice-the-debts.png. Desktop (UI Automation, Test\\Tools\\SrsLj\\desktop_cui.ps1): the same numbers read from its window (window pictures come out blank at night).
- README: 'In the MAS App' and 'How to demonstrate it' (healthy vs distressed; one figure changed; a climate- or cyber-exposed company).
- QUESTIONS for the author: (1) the MAS Service runs ONE shared instance of each Module for all clients; with a continuously executing Module (CUI-CPP) two clients at once take each other's results (seen: the desktop's report did not update while the browser's session was open); (2) the CUI AIMs keep the latest of each Input, so a company WITHOUT climate, cyber or secondary-risk data inherits those of the company before it in the same Module (seen before the Service was restarted: the healthy company showed cyber and climate parts). For the demonstration: restart the Service, or choose the companies without those data first. Both are design questions of the Service/Module, not changed tonight.
- Related tests: Mpai.Cui.Tests 36/36; MasBoutTests, MasSrsAppTests, MasCaeAppsTests and the Remote* tests 27/28 (RemoteEndToEndTests.Step5TwoCavs fails, as in the whole run of 2026-11-17).""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
