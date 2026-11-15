# Results

Each folder holds one experiment's output, written by `rag-eval eval run experiments/<name>.json --out results/<name>`:
`summary.md` (the tables) and `results.json` (every query's metrics, the configuration, and the
environment: commit, CPU, Ollama and Qdrant versions, model digests). Only ids and numbers are
stored, no dataset text.

## Baseline (v0.1 pipeline)

[`baseline/summary.md`](baseline/summary.md): fixed-size chunks of 1,000 characters with 200
overlap, `nomic-embed-text` (v1.5, digest `0a109f422b47`), dense retrieval in Qdrant (cosine),
100 chunks per query. Apple M4, Ollama 0.40.16, Qdrant 1.20.0. Every experiment is compared
with these numbers.

| Dataset | Queries | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 | Answer hit@5 | Answer hit@10 |
|---|---:|---:|---|---:|---:|---:|---:|---:|
| SciFact | 300 | **0.714** | 0.670–0.754 | 0.848 | 0.943 | 0.678 | – | – |
| Turkish RAG Eval | 58 | **0.634** | 0.532–0.732 | 0.793 | 0.983 | 0.583 | 0.345 | 0.466 |
| Turkish RAG Eval, full *(provisional)* | 296 | **0.408** | 0.361–0.453 | 0.574 | 0.821 | 0.356 | 0.223 | 0.264 |

### What it says

**The pipeline measures what it should.** The model card of nomic-embed-text-v1.5 reports, on
MTEB SciFact, nDCG@10 0.703, recall@10 0.844, recall@100 0.928 and MRR@10 0.666
([model card](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5)). Ours are within a point
or two and the published nDCG@10 lies inside our confidence interval. MTEB embeds each abstract
whole; we embed chunks and rank a document by its best chunk, which can only add matching
chances, consistent with our slightly higher numbers.

**On Turkish, the right article is found but the right passage is not.** On the 58 questions,
the relevant article is in the top 100 for 98% of them and in the top 10 for 79%, but a chunk
containing the answer is in the top 5 for only 35% and the top 10 for 47%. This is not because
chunks cut answers in two: every one of the 58 answer spans lies entirely inside some chunk of
its article (293 of 296 in the full set), so a perfect ranking would reach 100%. The gap is
ranking: within long articles, the chunk that answers is outranked by others. That points the
experiments at the embedding model (#13), hybrid retrieval (#14) and reranking (#15) more than
at chunk boundaries (#12).

**The full Turkish set is much harder for this model** (0.408 vs 0.634). Its 238 extra questions
span five more domains and are LLM-drafted, so the numbers are provisional. The dataset's
author reports lower scores on the full set for most models too (for example
paraphrase-multilingual-MiniLM 0.501 → 0.405, Mursit-Large 0.781 → 0.613).

**Retrieval is fast:** embedding a query takes about 21 ms and the Qdrant search about 10 ms
(p50), so the answering model, not retrieval, will dominate end-to-end latency (#18).

**Noise:** with 58 queries the 95% interval is about ±0.1 nDCG@10; differences smaller than
that on the Turkish set are not evidence. SciFact (300 queries) resolves about ±0.04.

### Reproduce

```sh
docker compose up -d qdrant           # and the Ollama app, or the ollama service
ollama pull nomic-embed-text
dotnet run --project src/RagEval.Cli -- eval run experiments/baseline.json --out results/baseline
```

Indexing takes about 5 minutes for SciFact and 4 for both Turkish sets on an M4; later runs
reuse the indexes.
