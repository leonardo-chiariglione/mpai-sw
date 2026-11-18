"""Night 2026-11-18: N-31 done."""
from pathlib import Path

P = Path(r"D:\AI\Night\TODO.md")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
head = "### N-31 "
i = s.index(head); j = s.index("Status: ", i); k = s.index("\n", j)
s = s[:j] + "Status: done (2026-11-18 night: word times aligned on the Machine Speech; Fast and Ollama tests pass; one question in the note)" + s[k:]
i = s.index(head); j = s.index("\n### ", i + len(head))
s = s[:j].rstrip("\n") + "\n" + "NOTE 2026-11-18 08:05 (night): the Ollama tests after the training, on the GPU (llama3.2:3b): PgmConditionOllamaTests, PgmControllerTests, PgmLevelsDemo and PgmSliceTests.OpenTheDoorsWithOllama - 5/5 pass in 1 min 42 s (OpenTheDoorsWithOllama had failed at 03:00 inside the whole run, with the processor and the GPU busy). Nothing committed. Still a stand-in: the alignment is on the signal (pauses), not a phonetic aligner." + "\n" + s[j:]
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
