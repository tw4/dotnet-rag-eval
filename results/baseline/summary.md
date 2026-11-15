# baseline

The v0.1 pipeline: fixed-size chunks of 1,000 characters with 200 overlap, nomic-embed-text, dense retrieval in Qdrant (cosine).

Run 2026-11-15 10:02 UTC, 2.7 min, on Apple M4 · commit `5f82b00` · Ollama 0.40.16 · Qdrant 1.20.0
Models: `nomic-embed-text` (0a109f422b47)

## scifact (300 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Chunks | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| baseline (fixed-1000-200 / nomic-embed-text / dense) | **0.714** | 0.670–0.754 | 0.848 | 0.943 | 0.678 | 11,164 | reused | 21 | 9.7 |

## turkish-rag-eval (58 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| baseline (fixed-1000-200 / nomic-embed-text / dense) | **0.634** | 0.532–0.732 | 0.793 | 0.983 | 0.583 | 0.345 | 0.466 | 1,366 | reused | 23 | 10.8 |

## turkish-rag-eval-full (296 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| baseline (fixed-1000-200 / nomic-embed-text / dense) | **0.408** | 0.361–0.453 | 0.574 | 0.821 | 0.356 | 0.223 | 0.264 | 2,371 | 139 | 21 | 10.2 |

nDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. Answer hit@k: a top-k chunk from a relevant document contains the answer span. The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.
