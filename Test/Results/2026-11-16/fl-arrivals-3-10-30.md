# One trained AIM at a time: how the Back End learns (2026-11-16 22:30)

3 runs per cell; 4 cycles; each End User 10 cases at the first cycle and 5 more at each later one; the AIMs arrive one by one in a random order; label bank of 300 of the Back End's own inputs. First models on the test set: 66.0 ± 1.5 %, loss 2.195 ± 0.065. Test set: 400 cases no method sees. Mean ± standard deviation over the runs.

| Way | End Users | AIMs arrived | Accepted | Test accuracy after cycle 1 (%) | Test accuracy at the end (%) | Test loss at the end | Mark at the end | Minutes per run |
|---|---|---|---|---|---|---|---|---|
| Replace | 3 | 12 | 36% | 68.3 ± 0.5 | 68.2 ± 3.8 | 0.794 ± 0.142 | 3 of 3 | 0.3 |
| FedAsync a=0.2 | 3 | 12 | 89% | 68.9 ± 1.5 | 69.4 ± 2.2 | 1.389 ± 0.093 | 3 of 3 | 0.4 |
| FedAsync a=0.5 | 3 | 12 | 75% | 69.3 ± 0.1 | 70.5 ± 1.8 | 1.117 ± 0.128 | 3 of 3 | 0.4 |
| Label bank, neutral inputs | 3 | 12 | 100% | 66.5 ± 2.0 | 67.3 ± 1.2 | 0.691 ± 0.064 | 3 of 3 | 0.9 |
| Label bank, varied inputs | 3 | 12 | 97% | 65.7 ± 2.3 | 69.4 ± 2.0 | 0.660 ± 0.033 | 3 of 3 | 1.0 |
| Replace | 10 | 40 | 26% | 68.7 ± 1.6 | 72.1 ± 3.3 | 0.943 ± 0.251 | 3 of 3 | 0.9 |
| FedAsync a=0.2 | 10 | 40 | 93% | 69.8 ± 2.3 | 70.5 ± 3.5 | 1.128 ± 0.109 | 3 of 3 | 1.1 |
| FedAsync a=0.5 | 10 | 40 | 78% | 70.1 ± 1.5 | 71.0 ± 3.0 | 0.925 ± 0.097 | 3 of 3 | 1.1 |
| Label bank, neutral inputs | 10 | 40 | 99% | 68.2 ± 2.8 | 72.3 ± 2.8 | 0.573 ± 0.077 | 3 of 3 | 3.2 |
| Label bank, varied inputs | 10 | 40 | 92% | 66.9 ± 2.8 | 68.7 ± 1.8 | 0.610 ± 0.043 | 3 of 3 | 3.1 |
| Replace | 30 | 120 | 16% | 70.3 ± 1.4 | 69.2 ± 5.9 | 0.737 ± 0.087 | 3 of 3 | 2.3 |
| FedAsync a=0.2 | 30 | 120 | 96% | 69.5 ± 1.1 | 69.0 ± 3.1 | 1.115 ± 0.164 | 3 of 3 | 3.2 |
| FedAsync a=0.5 | 30 | 120 | 84% | 69.8 ± 0.8 | 70.4 ± 3.1 | 0.930 ± 0.158 | 3 of 3 | 3.1 |
| Label bank, neutral inputs | 30 | 120 | 66% | 66.8 ± 4.8 | 66.2 ± 5.6 | 0.630 ± 0.058 | 3 of 3 | 8.3 |
| Label bank, varied inputs | 30 | 120 | 99% | 66.8 ± 3.4 | 67.1 ± 3.1 | 0.605 ± 0.041 | 3 of 3 | 9.0 |

Time: 11.0 min.
