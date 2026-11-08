# Roadmap

Each milestone has a definition of done. Issues are linked from the
[milestones page](../../milestones).

## v0.1 — Baseline pipeline

- Solution skeleton: API, ingestion worker, evaluation CLI, tests, CI
- `docker compose up` with Qdrant and Ollama
- Ingestion for Markdown and HTML, keeping source positions
- Fixed-size chunking with overlap
- Embeddings through Ollama, stored in Qdrant
- Question endpoint: retrieve top-k and answer with citations

**Done when:** a folder of documents can be ingested and questioned end to end on a
laptop, with citations pointing at the right source.

## v0.2 — Retrieval evaluation

- Choose the evaluation datasets (public, with a licence that allows redistribution of
  results); one English and one Turkish if a suitable one exists
- Dataset loader and corpus ingestion
- Metrics: recall@k, MRR, nDCG@10
- `eval run` command writing JSON results and a Markdown summary
- Baseline numbers for the v0.1 pipeline

**Done when:** the baseline retrieval numbers are reproducible with one command.

## v0.3 — Experiments

- Chunking strategies: fixed, recursive by structure, sentence-window
- Embedding models, including a multilingual one
- Sparse and hybrid retrieval with rank fusion in Qdrant
- Reranking with a local model, with its latency cost
- One results table per experiment

**Done when:** each table in the README is produced by a committed experiment config.

## v0.4 — Answer evaluation

- Faithfulness and correctness judge using a local model
- Hand-labelled sample to measure judge agreement before trusting it
- Latency per stage (p50/p95)
- PDF ingestion

## v1.0 — Report

- Write-up of what helped, what did not, and by how much
- Reproducible scripts and pinned model versions
- CI that runs a small evaluation subset on every change
