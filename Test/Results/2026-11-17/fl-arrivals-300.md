# One trained AIM at a time: how the Back End learns (2026-11-17 02:08)

3 runs per cell; 4 cycles; each End User 10 cases at the first cycle and 5 more at each later one; the AIMs arrive one by one in a random order; label bank of 300 of the Back End's own inputs. First models on the test set: 66.0 ± 1.5 %, loss 2.195 ± 0.065. Test set: 400 cases no method sees. Mean ± standard deviation over the runs.

| Way | End Users | AIMs arrived | Accepted | Test accuracy after cycle 1 (%) | Test accuracy at the end (%) | Test loss at the end | Mark at the end | Minutes per run |
|---|---|---|---|---|---|---|---|---|
| Replace | 300 | 1200 | 12% | 68.6 ± 0.9 | 71.5 ± 2.0 | 0.702 ± 0.107 | 3 of 3 | 23.2 |
| FedAsync a=0.2 | 300 | 1200 | 100% | 69.8 ± 1.3 | 69.1 ± 4.1 | 0.945 ± 0.144 | 3 of 3 | 29.5 |
| FedAsync a=0.5 | 300 | 1200 | 98% | 70.3 ± 1.8 | 69.2 ± 3.4 | 0.909 ± 0.072 | 3 of 3 | 29.4 |
| Label bank, neutral inputs | 300 | 1200 | 47% | 70.2 ± 3.1 | 69.1 ± 4.1 | 0.597 ± 0.044 | 3 of 3 | 38.2 |
| Label bank, varied inputs | 300 | 1200 | 56% | 67.7 ± 1.9 | 65.0 ± 3.9 | 0.637 ± 0.037 | 3 of 3 | 45.7 |

Time: 56.7 min.
