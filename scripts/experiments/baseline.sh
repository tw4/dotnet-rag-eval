#!/usr/bin/env bash
# The v0.1 pipeline every experiment is compared with (#11): experiments/baseline.json -> results/baseline/
exec "$(dirname "$0")/run.sh" run baseline "$@"
