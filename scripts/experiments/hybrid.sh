#!/usr/bin/env bash
# BM25 and hybrid retrieval (#14): experiments/hybrid.json -> results/hybrid/
exec "$(dirname "$0")/run.sh" run hybrid "$@"
