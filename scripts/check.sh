#!/usr/bin/env bash
# Everything a change must pass, run locally (there is no CI): formatting, a warning-free
# Release build and the test suite. Usage: ./scripts/check.sh
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> format"
dotnet format RagEval.slnx --verify-no-changes

echo "==> build (Release, warnings as errors)"
dotnet build RagEval.slnx -c Release --nologo -v quiet

echo "==> test"
dotnet test --solution RagEval.slnx -c Release --no-build

echo "All checks passed."
