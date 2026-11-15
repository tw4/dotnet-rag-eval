# hybrid

BM25 (sparse vectors in Qdrant with IDF), dense retrieval with the baseline and the best model from #13, and reciprocal rank fusion (k = 60, 100 candidates per side) of the two, on structural chunks of 1,000 characters. BM25 runs with a plain analyzer and with terms cut to their first five characters, a crude stemmer for Turkish suffixes.

Run 2026-11-15 15:39 UTC, 1.5 min, on Apple M4 · commit `943c8a5` · Ollama 0.40.16 · Qdrant 1.20.0
Models: `nomic-embed-text` (0a109f422b47), `embeddinggemma` (85462619ee72)

## scifact (300 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| dense nomic-embed-text | **0.715** | 0.671–0.757 | 0.840 | 0.942 | 0.682 | 10,768 | 7.77 | 33.1 | 373 | 20 | 9.5 |
| dense embeddinggemma | **0.768** | 0.730–0.805 | 0.888 | 0.962 | 0.736 | 10,768 | 7.77 | 33.1 | 318 | 23 | 10.4 |
| bm25 plain | **0.638** | 0.592–0.682 | 0.759 | 0.870 | 0.608 | 10,768 | 7.77 | 6.1 | 2 | – | 4.1 |
| bm25 prefix-5 | **0.675** | 0.632–0.719 | 0.801 | 0.899 | 0.642 | 10,768 | 7.77 | 5.8 | 1 | – | 4.1 |
| hybrid nomic + bm25 prefix-5 | **0.724** | 0.684–0.765 | 0.847 | 0.955 | 0.693 | 10,768 | 7.77 | 38.9 | 374 | 20 | 8.4 |
| hybrid embeddinggemma + bm25 prefix-5 | **0.743** | 0.703–0.782 | 0.855 | 0.978 | 0.717 | 10,768 | 7.77 | 38.9 | 319 | 23 | 9.0 |

## turkish-rag-eval (58 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| dense nomic-embed-text | **0.687** | 0.593–0.781 | 0.862 | 0.983 | 0.631 | 0.431 | 0.466 | 1,649 | 1.08 | 5.1 | 77 | 20 | 8.3 |
| dense embeddinggemma | **0.988** | 0.963–1.000 | 1.000 | 1.000 | 0.984 | 0.966 | 0.966 | 1,649 | 1.08 | 5.1 | 62 | 25 | 11.3 |
| bm25 plain | **0.764** | 0.681–0.842 | 0.931 | 1.000 | 0.710 | 0.483 | 0.586 | 1,649 | 1.08 | 0.9 | 0 | – | 5.1 |
| bm25 prefix-5 | **0.884** | 0.819–0.938 | 0.983 | 1.000 | 0.852 | 0.552 | 0.672 | 1,649 | 1.08 | 0.8 | 0 | – | 4.4 |
| hybrid nomic + bm25 prefix-5 | **0.839** | 0.760–0.909 | 0.931 | 1.000 | 0.809 | 0.569 | 0.724 | 1,649 | 1.08 | 5.9 | 77 | 21 | 8.3 |
| hybrid embeddinggemma + bm25 prefix-5 | **0.943** | 0.901–0.975 | 1.000 | 1.000 | 0.924 | 0.810 | 0.897 | 1,649 | 1.08 | 5.9 | 62 | 23 | 10.5 |

## turkish-rag-eval-full (296 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| dense nomic-embed-text | **0.426** | 0.381–0.475 | 0.595 | 0.838 | 0.374 | 0.247 | 0.294 | 2,775 | 1.87 | 8.5 | 137 | 21 | 9.3 |
| dense embeddinggemma | **0.899** | 0.874–0.924 | 0.990 | 0.990 | 0.870 | 0.838 | 0.902 | 2,775 | 1.87 | 8.5 | 94 | 21 | 9.8 |
| bm25 plain | **0.675** | 0.636–0.717 | 0.872 | 0.980 | 0.613 | 0.534 | 0.608 | 2,775 | 1.87 | 1.6 | 0 | – | 4.3 |
| bm25 prefix-5 | **0.809** | 0.776–0.846 | 0.946 | 0.997 | 0.766 | 0.666 | 0.753 | 2,775 | 1.87 | 1.4 | 0 | – | 4.5 |
| hybrid nomic + bm25 prefix-5 | **0.645** | 0.604–0.687 | 0.872 | 0.997 | 0.575 | 0.416 | 0.595 | 2,775 | 1.87 | 10.0 | 137 | 19 | 8.6 |
| hybrid embeddinggemma + bm25 prefix-5 | **0.890** | 0.866–0.914 | 0.983 | 1.000 | 0.858 | 0.807 | 0.875 | 2,775 | 1.87 | 10.0 | 95 | 22 | 9.4 |

nDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. Answer hit@k: a top-k chunk from a relevant document contains the answer span. Text: characters stored in chunk payloads; Vectors: raw vector size (dense: 4 bytes per dimension; sparse: 8 bytes per non-zero term); Index: time to build the index. The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.
