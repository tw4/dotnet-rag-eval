#!/usr/bin/env bash
# Local embedding models (#13): experiments/embeddings.json -> results/embeddings/
exec "$(dirname "$0")/run.sh" run embeddings "$@"
