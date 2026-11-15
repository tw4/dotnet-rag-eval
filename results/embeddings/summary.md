# embeddings

Embedding models served by Ollama, each with the prompts its model card prescribes, on structural chunks of 1,000 characters (the choice from #12) and dense retrieval. nomic-embed-text and mxbai-embed-large are English models; bge-m3, qwen3-embedding and embeddinggemma are multilingual.

Run 2026-11-15 13:34 UTC, 90.8 min, on Apple M4 · commit `d19c599` · Ollama 0.40.16 · Qdrant 1.20.0
Models: `nomic-embed-text` (0a109f422b47), `mxbai-embed-large` (468836162de7), `bge-m3` (790764642607), `qwen3-embedding:0.6b` (ac6da0dfba84), `embeddinggemma` (85462619ee72)

## scifact (300 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| nomic-embed-text (137M, 768d) | **0.715** | 0.671–0.757 | 0.840 | 0.942 | 0.682 | 10,768 | 7.77 | 33.1 | 373 | 22 | 10.0 |
| mxbai-embed-large (335M, 1024d) | **0.742** | 0.701–0.784 | 0.875 | 0.948 | 0.706 | 10,768 | 7.77 | 44.1 | 787 | 24 | 11.5 |
| bge-m3 (568M, 1024d, multilingual) | **0.658** | 0.613–0.703 | 0.801 | 0.887 | 0.619 | 10,768 | 7.77 | 44.1 | 1073 | 28 | 11.5 |
| qwen3-embedding:0.6b (596M, 1024d, multilingual) | **0.695** | 0.651–0.737 | 0.833 | 0.933 | 0.655 | 10,768 | 7.77 | 44.1 | 1166 | 39 | 10.3 |
| embeddinggemma (308M, 768d, multilingual) | **0.768** | 0.730–0.805 | 0.888 | 0.962 | 0.736 | 10,768 | 7.77 | 33.1 | 318 | 20 | 9.2 |

## turkish-rag-eval (58 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| nomic-embed-text (137M, 768d) | **0.687** | 0.593–0.781 | 0.862 | 0.983 | 0.631 | 0.431 | 0.466 | 1,649 | 1.08 | 5.1 | 77 | 18 | 8.3 |
| mxbai-embed-large (335M, 1024d) | **0.523** | 0.419–0.626 | 0.707 | 0.931 | 0.465 | 0.224 | 0.362 | 1,649 | 1.08 | 6.8 | 230 | 54 | 11.5 |
| bge-m3 (568M, 1024d, multilingual) | **0.932** | 0.877–0.977 | 0.983 | 1.000 | 0.916 | 0.845 | 0.897 | 1,649 | 1.08 | 6.8 | 132 | 22 | 9.5 |
| qwen3-embedding:0.6b (596M, 1024d, multilingual) | **0.815** | 0.724–0.894 | 0.897 | 0.983 | 0.788 | 0.638 | 0.690 | 1,649 | 1.08 | 6.8 | 239 | 40 | 11.2 |
| embeddinggemma (308M, 768d, multilingual) | **0.988** | 0.963–1.000 | 1.000 | 1.000 | 0.984 | 0.966 | 0.966 | 1,649 | 1.08 | 5.1 | 62 | 16 | 8.5 |

## turkish-rag-eval-full (296 queries)

| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 | Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| nomic-embed-text (137M, 768d) | **0.426** | 0.381–0.475 | 0.595 | 0.838 | 0.374 | 0.247 | 0.294 | 2,775 | 1.87 | 8.5 | 137 | 21 | 10.2 |
| mxbai-embed-large (335M, 1024d) | **0.364** | 0.321–0.408 | 0.537 | 0.774 | 0.310 | 0.159 | 0.240 | 2,775 | 1.87 | 11.4 | 442 | 38 | 11.1 |
| bge-m3 (568M, 1024d, multilingual) | **0.874** | 0.847–0.900 | 0.980 | 0.993 | 0.839 | 0.804 | 0.885 | 2,775 | 1.87 | 11.4 | 326 | 35 | 11.1 |
| qwen3-embedding:0.6b (596M, 1024d, multilingual) | **0.749** | 0.713–0.786 | 0.922 | 0.973 | 0.693 | 0.564 | 0.666 | 2,775 | 1.87 | 11.4 | 446 | 35 | 11.8 |
| embeddinggemma (308M, 768d, multilingual) | **0.899** | 0.874–0.924 | 0.990 | 0.990 | 0.870 | 0.838 | 0.902 | 2,775 | 1.87 | 8.5 | 94 | 20 | 9.4 |

nDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. Answer hit@k: a top-k chunk from a relevant document contains the answer span. Text: characters stored in chunk payloads; Vectors: raw float32 vector size; Index: time to build the index. The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.
