# One trained AIM at a time: how the Back End learns (2026-11-16 22:56)

3 runs per cell; 4 cycles; each End User 10 cases at the first cycle and 5 more at each later one; the AIMs arrive one by one in a random order; label bank of 300 of the Back End's own inputs. First models on the test set: 66.0 ± 1.5 %, loss 2.195 ± 0.065. Test set: 400 cases no method sees. Mean ± standard deviation over the runs.

| Way | End Users | AIMs arrived | Accepted | Test accuracy after cycle 1 (%) | Test accuracy at the end (%) | Test loss at the end | Mark at the end | Minutes per run |
|---|---|---|---|---|---|---|---|---|
| Replace | 100 | 400 | 13% | 69.9 ± 1.8 | 69.2 ± 5.0 | 0.779 ± 0.216 | 3 of 3 | 8.1 |
| FedAsync a=0.2 | 100 | 400 | 100% | 69.7 ± 1.5 | 68.7 ± 4.6 | 1.012 ± 0.216 | 3 of 3 | 10.9 |
| FedAsync a=0.5 | 100 | 400 | 92% | 69.6 ± 1.4 | 69.0 ± 4.2 | 0.995 ± 0.227 | 3 of 3 | 10.7 |
| Label bank, neutral inputs | 100 | 400 | 77% | 68.4 ± 1.0 | 68.9 ± 5.8 | 0.602 ± 0.068 | 3 of 3 | 19.2 |
| Label bank, varied inputs | 100 | 400 | 83% | 66.3 ± 4.3 | 66.8 ± 3.1 | 0.609 ± 0.038 | 3 of 3 | 22.8 |

Time: 25.4 min.
