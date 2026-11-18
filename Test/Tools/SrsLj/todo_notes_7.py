"""Night 2026-11-18: N-32 note (stays todo)."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
head = "### N-32 "
i = s.index(head); j = s.index("\n### ", i + len(head))
s = s[:j].rstrip("\n") + "\n" + """NOTE 2026-11-18 (night): NOT STARTED, stays todo. The video is drawn by Edge on the GPU (about 70 min), and the GPU was taken all night by the five N-27 trainings (the last ended about 08:00; no new request may start after 08:00). Done meanwhile: the two remaining "two metaverses" in the demo's comments (TwoMetaverses\\Program.cs, stage\\index.html) say "Real-Virtual Environments"; the README already did. Found for the deck step: the deck in OneDrive (MPAI-XRV-LTP presentation.pptx) was changed on 2026-11-16 19:04 - SHA-256 98DDC227..., not the 0aa2f378... of this request - so the new video will go into an edited COPY in Downloads, as the request says. Question: N-38 already made "XRV-LTP Real-Virtual Environments demo.mp4" (the live show of 2026-11-16, with the larger Theatre); is the re-render of the 2026-11-15 recording still wanted, or should the deck get the N-38 video?""" + "\n" + s[j:]
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
