#!/usr/bin/env bash
# Answer faithfulness and correctness, judged by a local model (#16): experiments/answers.json -> results/answers/
exec "$(dirname "$0")/run.sh" answers answers "$@"
