#!/usr/bin/env bash
# Latency per stage (#18): experiments/latency.json -> results/latency/
exec "$(dirname "$0")/run.sh" latency latency "$@"
