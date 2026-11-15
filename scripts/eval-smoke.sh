#!/usr/bin/env bash
# A small, fast evaluation to catch retrieval regressions before merging, run locally (there is no
# CI). It runs experiments/smoke.json and compares every metric with the committed reference in
# results/smoke/; the pipeline is deterministic, so a drop means behaviour changed.
#
# Needs Qdrant and Ollama running (docker compose up -d qdrant, and the Ollama app or service)
# and the models in experiments/smoke.json. Indexes are reused, so after the first run it takes
# well under a minute.
#
# Usage: ./scripts/eval-smoke.sh [--tolerance 0.01]
#   --update   accept the current numbers as the new reference (after an intended change)
set -euo pipefail
cd "$(dirname "$0")/.."

tolerance=0.01
update=false
while [ $# -gt 0 ]; do
  case "$1" in
    --tolerance) tolerance="$2"; shift 2 ;;
    --update) update=true; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

qdrant="${QDRANT_URL:-http://localhost:6333}"
ollama="${OLLAMA_URL:-http://localhost:11434}"
curl -fsS "$qdrant/healthz" > /dev/null || { echo "Qdrant is not reachable at $qdrant (docker compose up -d qdrant)." >&2; exit 2; }
models=$(curl -fsS "$ollama/api/tags") || { echo "Ollama is not reachable at $ollama." >&2; exit 2; }
for model in $(grep -o '"model": *"[^"]*"' experiments/smoke.json | sed 's/.*"\([^"]*\)"$/\1/' | sort -u); do
  case "$models" in
    *"\"$model\""*|*"\"$model:latest\""*) ;;
    *) echo "Model $model is missing: ./scripts/pull-models.sh, or ollama pull $model." >&2; exit 2 ;;
  esac
done

cli=(dotnet run --project src/RagEval.Cli -c Release --)
"${cli[@]}" datasets download turkish-rag-eval > /dev/null
out=runs/smoke/latest
rm -rf "$out"
"${cli[@]}" eval run experiments/smoke.json --out "$out" | grep -E '^(turkish|  index|  indexed)' || true

if $update; then
  mkdir -p results/smoke
  cp "$out/results.json" "$out/summary.md" results/smoke/
  echo "Reference updated: results/smoke/. Commit it with the change that moved the numbers."
  exit 0
fi

"${cli[@]}" eval compare "$out/results.json" results/smoke/results.json --tolerance "$tolerance"
