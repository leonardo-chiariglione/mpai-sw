"""Night 2026-11-18: N-33 blocked, with the question."""
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


status("### N-33 ", "blocked (2026-11-18 night: the request averages models in rounds; later decisions say otherwise - question in the note)")
note_before_next("### N-33 ", "NOTE 2026-11-18 (night): BLOCKED, nothing run. The request (2026-11-15) has the Back End average the End Users' updates in rounds (\"HFL averages the updates weighted by number of cases\", FedAvg in experiment 2). After it was written the author said: \"HBE only receives one trained AIM\" (2026-11-16, N-39 built on it), that averaging is a poor solution and an NNM received from an HFE is to TRAIN the HBE's NNM, that the HBE should use no X-rays of its own, and \"lets stop here\" (2026-11-17). Running N-33 as written would test what you have since set aside. Question: (a) run it as written (FedAvg rounds) for the comparison with the local and central models; (b) run it with one trained AIM arriving at a time, by the ways of N-39 (Replace, FedAsync, label bank) - no averaging of a round; (c) only the parts that do not depend on the method: the per-patient splits, the start model, the local models (distribution of AUC), the central model, and experiment 2's central fine-tuning of the last DenseNet block; or (d) leave HSP for now. The preparation (features and block-4 inputs of 34,999 images) is ready for any of them.")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
