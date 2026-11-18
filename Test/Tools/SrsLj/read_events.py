"""N-27: read a Lightning TensorBoard events file; print the last step, wall time and the losses (or a JSON summary with --json).
python read_events.py <events file> [--json]"""
import json
import sys

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

acc = EventAccumulator(sys.argv[1], size_guidance={"scalars": 0})
acc.Reload()
out = {}
for tag in acc.Tags()["scalars"]:
    ev = acc.Scalars(tag)
    out[tag] = {"first": [ev[0].step, round(ev[0].wall_time, 1), round(ev[0].value, 4)],
                "last": [ev[-1].step, round(ev[-1].wall_time, 1), round(ev[-1].value, 4)],
                "n": len(ev)}
if "--json" in sys.argv:
    full = {tag: [[e.step, round(e.wall_time, 1), round(e.value, 5)] for e in acc.Scalars(tag)] for tag in acc.Tags()["scalars"]}
    print(json.dumps(full))
else:
    for k, v in out.items():
        print(k, v)
