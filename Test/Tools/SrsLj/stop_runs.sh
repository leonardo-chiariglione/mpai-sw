#!/bin/bash
# N-27: stop my own training runs (train_fixed) - nothing else.
pkill -f train_one.sh; pkill -f train_fixed; sleep 3; pgrep -af train_fixed || echo stopped
