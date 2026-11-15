#!/usr/bin/env bash
# Reranking and its latency (#15): experiments/rerank.json -> results/rerank/
exec "$(dirname "$0")/run.sh" run rerank "$@"
