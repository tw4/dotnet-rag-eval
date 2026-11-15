#!/usr/bin/env bash
# Pulls the models in models/models.lock into the local Ollama and checks their digests.
#
#   ./scripts/pull-models.sh                 every model
#   ./scripts/pull-models.sh bge-m3 qwen3:4b only these
#   ./scripts/pull-models.sh --verify ...    pull nothing, only check what is installed
#
# qwen3-reranker:0.6b is not in the Ollama library: it is built from the official Hugging Face
# weights at a pinned revision, checked against models/qwen3-reranker/SHA256SUMS. Its digest
# depends on how Ollama converts the weights, so a different Ollama version may give another
# digest for the same weights; that is reported as a warning only.
set -euo pipefail
cd "$(dirname "$0")/.."

ollama_url="${OLLAMA_URL:-http://localhost:11434}"
reranker_repo="Qwen/Qwen3-Reranker-0.6B"
reranker_revision="e61197ed45024b0ed8a2d74b80b4d909f1255473"
cache="${RAG_EVAL_MODELS:-$HOME/.cache/rag-eval/models}"

verify_only=false
if [ "${1:-}" = "--verify" ]; then verify_only=true; shift; fi

normalize() { case "$1" in *:*) echo "$1" ;; *) echo "$1:latest" ;; esac; }
wanted=()
for m in "$@"; do wanted+=("$(normalize "$m")"); done
# Bash 3 (macOS) treats empty arrays as unset under "set -u": always expand with ${a[@]+"${a[@]}"}.

installed_digest() {
  curl -fsS "$ollama_url/api/tags" | tr '{' '\n' | grep "\"name\":\"$1\"" | sed -n 's/.*"digest":"\([0-9a-f]*\)".*/\1/p' | head -1
}

build_reranker() {
  local dir="$cache/qwen3-reranker-0.6b"
  mkdir -p "$dir"
  while read -r sum file; do
    if [ ! -f "$dir/$file" ] || ! echo "$sum  $dir/$file" | shasum -a 256 -c --status; then
      echo "  downloading $reranker_repo/$file @ ${reranker_revision:0:8}"
      curl -fsSL -o "$dir/$file" "https://huggingface.co/$reranker_repo/resolve/$reranker_revision/$file"
    fi
  done < models/qwen3-reranker/SHA256SUMS
  (cd "$dir" && shasum -a 256 -c --quiet "$OLDPWD/models/qwen3-reranker/SHA256SUMS")
  cp models/qwen3-reranker/Modelfile "$dir/Modelfile"
  (cd "$dir" && ollama create qwen3-reranker:0.6b -f Modelfile > /dev/null 2>&1)
}

curl -fsS "$ollama_url/api/version" > /dev/null || { echo "Ollama is not reachable at $ollama_url." >&2; exit 2; }
status=0
matched=()
while read -r name digest role; do
  case "$name" in ''|'#'*) continue ;; esac
  if [ ${#wanted[@]} -gt 0 ] && [[ ! " ${wanted[*]-} " == *" $name "* ]]; then continue; fi
  matched+=("$name")
  if ! $verify_only; then
    echo "pulling $name ($role)"
    if [ "$name" = "qwen3-reranker:0.6b" ]; then build_reranker; else ollama pull "$name" > /dev/null 2>&1 || echo "  pull failed: $name" >&2; fi
  fi
  actual="$(installed_digest "$name")"
  if [ -z "$actual" ]; then
    echo "  MISSING  $name" >&2; status=1
  elif [ "$actual" = "$digest" ]; then
    echo "  ok       $name ${digest:0:12}"
  elif [ "$name" = "qwen3-reranker:0.6b" ]; then
    echo "  warning  $name ${actual:0:12}, published results used ${digest:0:12} (same weights, other Ollama conversion?)"
  else
    echo "  CHANGED  $name ${actual:0:12}, published results used ${digest:0:12}: the tag moved upstream" >&2; status=1
  fi
done < models/models.lock

for m in ${wanted[@]+"${wanted[@]}"}; do
  if [[ ! " ${matched[*]-} " == *" $m "* ]]; then echo "Not in models/models.lock: $m" >&2; status=1; fi
done
exit $status
