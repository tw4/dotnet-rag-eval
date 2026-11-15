#!/usr/bin/env bash
# Chunking strategies (#12): experiments/chunking.json -> results/chunking/
exec "$(dirname "$0")/run.sh" run chunking "$@"
