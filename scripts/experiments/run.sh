#!/usr/bin/env bash
# Runs one experiment from experiments/<name>.json the way its published results were made:
# checks the services, the models (digests from models/models.lock) and the datasets, then
# writes results/<name>/. The per-experiment scripts next to this one call it.
#
# Usage: scripts/experiments/run.sh <run|answers|latency> <name> [extra CLI options]
set -euo pipefail
cd "$(dirname "$0")/../.."

kind="$1"; name="$2"; shift 2
config="experiments/$name.json"
[ -f "$config" ] || { echo "No $config." >&2; exit 2; }

qdrant="${QDRANT_URL:-http://localhost:6333}"
curl -fsS "$qdrant/healthz" > /dev/null || { echo "Qdrant is not reachable at $qdrant (docker compose up -d qdrant)." >&2; exit 2; }

# Every model the experiment names must be installed with the pinned digest.
models=$(grep -oE '"(model|chatModel)": *"[^"]*"' "$config" | sed 's/.*"\([^"]*\)"$/\1/' | sort -u)
# shellcheck disable=SC2086
./scripts/pull-models.sh --verify $models || {
  echo "Install the models first: ./scripts/pull-models.sh $(echo $models)" >&2; exit 2; }

if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  echo "warning: uncommitted changes; the results will record a dirty commit." >&2
fi

cli=(dotnet run --project src/RagEval.Cli -c Release --)
datasets=$(grep -oE '"datasets?": *(\[[^]]*\]|"[^"]*")' "$config" | grep -oE '"[a-z0-9-]+"' | grep -v '"datasets\?"' | tr -d '"' | sort -u)
# shellcheck disable=SC2086
"${cli[@]}" datasets download $datasets

case "$kind" in
  run) "${cli[@]}" eval run "$config" --out "results/$name" "$@" ;;
  answers) "${cli[@]}" eval answers "$config" --out "results/$name" "$@" ;;
  latency) "${cli[@]}" eval latency "$config" --out "results/$name" "$@" ;;
  *) echo "Unknown kind '$kind': run, answers or latency." >&2; exit 2 ;;
esac
