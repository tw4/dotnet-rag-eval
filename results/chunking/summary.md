# chunking

Chunking strategies with the baseline embedding model (nomic-embed-text) and dense retrieval: fixed windows of three sizes, structural (sections, paragraphs, sentences), and sentence windows (one sentence embedded, two sentences on each side returned).

Run 2026-11-15 10:10 UTC, 51.5 min, on Apple M4 · commit `48172a0` · Ollama 0.40.16 · Qdrant 1.20.0
Models: `nomic-embed-text` (0a109f422b47)

## scifact (300 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fixed-500-100 | **0.713** | 0.670–0.754 | 0.837 | 0.933 | 0.680 | 20,666 | 9.26 | 63.5 | 456 | 20 | 9.0 |
| fixed-1000-200 (baseline) | **0.714** | 0.670–0.754 | 0.848 | 0.943 | 0.678 | 11,164 | 8.95 | 34.3 | 312 | 21 | 9.2 |
| fixed-2000-400 | **0.698** | 0.656–0.739 | 0.840 | 0.933 | 0.661 | 5,977 | 8.09 | 18.4 | 321 | 20 | 9.8 |
| structural-1000 | **0.715** | 0.671–0.757 | 0.840 | 0.942 | 0.682 | 10,768 | 7.77 | 33.1 | 373 | 21 | 9.1 |
| sentence-window-2 | **0.695** | 0.651–0.738 | 0.824 | 0.917 | 0.662 | 49,888 | 34.35 | 153.3 | 709 | 20 | 8.8 |

## turkish-rag-eval (58 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fixed-500-100 | **0.666** | 0.563–0.760 | 0.810 | 0.983 | 0.621 | 0.362 | 0.466 | 2,718 | 1.34 | 8.3 | 92 | 12 | 6.5 |
| fixed-1000-200 (baseline) | **0.634** | 0.532–0.732 | 0.793 | 0.983 | 0.583 | 0.345 | 0.466 | 1,366 | 1.34 | 4.2 | 97 | 22 | 12.0 |
| fixed-2000-400 | **0.647** | 0.546–0.746 | 0.810 | 0.983 | 0.596 | 0.414 | 0.534 | 689 | 1.33 | 2.1 | 93 | 16 | 11.8 |
| structural-1000 | **0.687** | 0.593–0.781 | 0.862 | 0.983 | 0.631 | 0.431 | 0.466 | 1,649 | 1.08 | 5.1 | 77 | 13 | 7.6 |
| sentence-window-2 | **0.689** | 0.589–0.781 | 0.845 | 1.000 | 0.640 | 0.414 | 0.431 | 9,635 | 5.38 | 29.6 | 145 | 19 | 10.2 |

## turkish-rag-eval-full (296 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fixed-500-100 | **0.431** | 0.386–0.476 | 0.618 | 0.848 | 0.373 | 0.189 | 0.270 | 4,716 | 2.32 | 14.5 | 164 | 20 | 9.6 |
| fixed-1000-200 (baseline) | **0.408** | 0.361–0.453 | 0.574 | 0.821 | 0.356 | 0.223 | 0.264 | 2,371 | 2.32 | 7.3 | 139 | 22 | 10.1 |
| fixed-2000-400 | **0.378** | 0.334–0.424 | 0.534 | 0.780 | 0.330 | 0.220 | 0.274 | 1,195 | 2.31 | 3.7 | 155 | 25 | 14.5 |
| structural-1000 | **0.426** | 0.381–0.475 | 0.595 | 0.838 | 0.374 | 0.247 | 0.294 | 2,775 | 1.87 | 8.5 | 137 | 20 | 10.3 |
| sentence-window-2 | **0.507** | 0.464–0.554 | 0.703 | 0.909 | 0.445 | 0.260 | 0.331 | 16,781 | 9.33 | 51.6 | 266 | 21 | 9.7 |

nDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. Answer hit@k: a top-k chunk from a relevant document contains the answer span. Text: characters stored in chunk payloads; Vectors: raw float32 vector size; Index: time to build the index. The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.
