# One trained AIM at a time: how the Back End learns (2026-11-17 22:16)

3 runs per cell; 4 cycles; each End User 10 cases at the first cycle and 5 more at each later one; the AIMs arrive one by one in a random order; label bank of 300 of the Back End's own inputs. First models on the test set: 66.0 ± 1.5 %, loss 2.195 ± 0.065. Test set: 400 cases no method sees. Mean ± standard deviation over the runs.

| Way | End Users | AIMs arrived | Accepted | Test accuracy after cycle 1 (%) | Test accuracy at the end (%) | Test loss at the end | Mark at the end | Minutes per run |
|---|---|---|---|---|---|---|---|---|
| Replace | 3 | 12 | 36% | 68.3 ± 0.5 | 68.2 ± 3.8 | 0.794 ± 0.142 | 3 of 3 | 0.3 |
| FedAsync a=0.2 | 3 | 12 | 89% | 68.9 ± 1.5 | 69.4 ± 2.2 | 1.389 ± 0.093 | 3 of 3 | 0.4 |
| FedAsync a=0.5 | 3 | 12 | 75% | 69.3 ± 0.1 | 70.5 ± 1.8 | 1.117 ± 0.128 | 3 of 3 | 0.4 |
| Learning added, step 1 | 3 | 12 | 39% | 69.5 ± 2.0 | 68.4 ± 1.8 | 0.984 ± 0.062 | 3 of 3 | 0.3 |
| Learning added, step 0.5 | 3 | 12 | 81% | 69.8 ± 0.6 | 67.7 ± 6.4 | 0.976 ± 0.133 | 3 of 3 | 0.4 |
| Learning added, step 0.2 | 3 | 12 | 92% | 68.8 ± 1.3 | 69.5 ± 3.0 | 1.352 ± 0.177 | 3 of 3 | 0.4 |
| Replace | 10 | 40 | 26% | 68.7 ± 1.6 | 72.1 ± 3.3 | 0.943 ± 0.251 | 3 of 3 | 0.9 |
| FedAsync a=0.2 | 10 | 40 | 93% | 69.8 ± 2.3 | 70.5 ± 3.5 | 1.128 ± 0.109 | 3 of 3 | 1.2 |
| FedAsync a=0.5 | 10 | 40 | 78% | 70.1 ± 1.5 | 71.0 ± 3.0 | 0.925 ± 0.097 | 3 of 3 | 1.1 |
| Learning added, step 1 | 10 | 40 | 42% | 68.7 ± 0.8 | 69.9 ± 3.6 | 0.725 ± 0.029 | 3 of 3 | 1.0 |
| Learning added, step 0.5 | 10 | 40 | 77% | 68.9 ± 3.2 | 71.4 ± 3.8 | 0.746 ± 0.094 | 3 of 3 | 1.1 |
| Learning added, step 0.2 | 10 | 40 | 97% | 69.8 ± 1.4 | 69.7 ± 4.1 | 0.999 ± 0.069 | 3 of 3 | 1.2 |
| Replace | 30 | 120 | 16% | 70.3 ± 1.4 | 69.2 ± 5.9 | 0.737 ± 0.087 | 3 of 3 | 2.5 |
| FedAsync a=0.2 | 30 | 120 | 96% | 69.5 ± 1.1 | 69.0 ± 3.1 | 1.115 ± 0.164 | 3 of 3 | 3.4 |
| FedAsync a=0.5 | 30 | 120 | 84% | 69.8 ± 0.8 | 70.4 ± 3.1 | 0.930 ± 0.158 | 3 of 3 | 3.2 |
| Learning added, step 1 | 30 | 120 | 50% | 68.4 ± 2.7 | 67.8 ± 2.0 | 0.828 ± 0.199 | 3 of 3 | 2.8 |
| Learning added, step 0.5 | 30 | 120 | 80% | 69.0 ± 3.6 | 57.7 ± 6.4 | 1.173 ± 0.215 | 3 of 3 | 3.2 |
| Learning added, step 0.2 | 30 | 120 | 99% | 68.8 ± 2.6 | 64.2 ± 1.9 | 0.731 ± 0.152 | 3 of 3 | 3.4 |

Time: 7.3 min.
