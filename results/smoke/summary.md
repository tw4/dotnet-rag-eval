# smoke

Smoke test: the chosen pipeline (structural chunks, embeddinggemma, dense retrieval) on the 58 human-written Turkish questions.

Run 2026-11-15 19:08 UTC, 0.0 min, on Apple M4 · commit `a0a490f` · Ollama 0.40.16 · Qdrant 1.20.0
Models: `embeddinggemma` (85462619ee72)

## turkish-rag-eval (58 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| structural-1000 / embeddinggemma / dense | **0.988** | 0.963–1.000 | 1.000 | 1.000 | 0.984 | 0.966 | 0.966 | 1,649 | 1.08 | 5.1 | 62 | 20 | 8.6 |

nDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. Answer hit@k: a top-k chunk from a relevant document contains the answer span. Text: characters stored in chunk payloads; Vectors: raw vector size (dense: 4 bytes per dimension; sparse: 8 bytes per non-zero term); Index: time to build the index. The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.
