# dotnet-rag-eval

**On-premises RAG in .NET, with every stage measured.**

Most retrieval-augmented generation examples stop at "it answered my question". This one is a
reference implementation for .NET teams that need to keep documents in-house, and it answers
the questions that come after the demo: which chunking works for these documents, which
embedding model, does hybrid search help, is the reranker worth its latency, and is the answer
actually supported by the retrieved text.

```
 documents ──► load ──► chunk ──► embed ──► Qdrant (dense + BM25 sparse)
 (md, html, pdf, txt)                            │
 question ──► embed ──► dense | BM25 | hybrid ───┴──► rerank ──► answer with citations
                                                                       │
              evaluation: retrieval metrics with CIs, LLM judge vs hand labels, p50/p95 per stage
```

Everything runs locally: models through Ollama, vectors in Qdrant, the app in .NET 10.

## Findings

Measured on [SciFact](docs/datasets.md) (English, 300 queries) and
[Turkish RAG Eval](docs/datasets.md) (Turkish, long articles; 58 hand-written questions, and
296 in the provisional full set) on an Apple M4. Every table, with confidence intervals, is in
[`results/`](results/README.md).

| Decision | Options compared | Result | Choice |
|---|---|---|---|
| Baseline ([#11](results/README.md#baseline-v01-pipeline)) | fixed 1,000-char chunks, nomic-embed-text, dense | SciFact nDCG@10 **0.714**, within the model card's published 0.703; Turkish full **0.408** | – |
| Chunking ([#12](results/README.md#chunking-12)) | fixed 500 / 1,000 / 2,000, structural, sentence windows | Does not matter on SciFact; on Turkish structural and sentence windows lead (full set 0.426 / 0.507 vs 0.408), sentence windows at 4–7× the vectors | `structural-1000` |
| Embedding model ([#13](results/README.md#embedding-models-13)) | nomic, mxbai-embed-large, bge-m3, qwen3-embedding:0.6b, embeddinggemma | **The largest lever.** Multilingual models double Turkish nDCG@10 (0.43 → 0.90); embeddinggemma is best on every set (SciFact 0.768) and the fastest to index | `embeddinggemma` |
| Hybrid search ([#14](results/README.md#hybrid-retrieval-14)) | BM25 (plain, prefix-5), dense, RRF fusion | BM25 reproduces BEIR (0.675 vs 0.665); a 5-character prefix adds 0.13 on Turkish. **Fusion does not beat dense embeddinggemma** (0.743 vs 0.768 on SciFact), but raises recall@100 | dense; BM25 when no multilingual model |

Negative results are kept: larger English embedding models get *worse* on Turkish
(mxbai-embed-large 0.364 vs nomic 0.426 on the full set), qwen3-embedding:0.6b trails the
smaller multilingual models, and the hybrid default many RAG guides recommend lowers nDCG@10
once the dense model is good. The 58-question Turkish set is saturated by the best pipeline
(nDCG@10 0.988), so the 296-question set carries Turkish comparisons.

**Not yet measured** (code, tests and experiment files are in place, the runs are not):
reranking with Qwen3-Reranker-0.6B ([#15](../../issues/15), about 3 passages per second
locally, so several seconds per question for 20 candidates), latency per stage
([#18](../../issues/18)), answer faithfulness and correctness by a local judge
([#16](../../issues/16)) and its agreement with hand labels ([#17](../../issues/17)).

## What is in it

| Stage | Implementation |
|---|---|
| **Ingestion** | Markdown, HTML, plain text and PDF (PdfPig; pages, bookmarks as headings), each chunk mapped back to source lines and pages |
| **Chunking** | Fixed windows on word boundaries; structural (sections → paragraphs → sentences, section path prepended); sentence windows |
| **Embeddings** | Any Ollama model, with the query/document prompts its model card prescribes |
| **Retrieval** | Dense (Qdrant cosine), BM25 as Qdrant sparse vectors with IDF, reciprocal rank fusion |
| **Reranking** | Pointwise yes/no reranker on log-probabilities (Qwen3-Reranker format) |
| **Answering** | `Microsoft.Extensions.AI` chat client, numbered citations to chunk and source position |
| **Evaluation** | nDCG@10, recall, MRR (trec_eval semantics), answer hit@k, bootstrap 95% CIs; LLM judge with JSON schema; Cohen's kappa against hand labels; Activity spans per stage |

Projects: `RagEval.Core` (chunking, embeddings, retrieval, reranking, answering, telemetry),
`RagEval.Ingestion` (loaders), `RagEval.Api` (ASP.NET Core minimal API), `RagEval.Cli`
(`rag-eval`), `RagEval.Evaluation` (datasets, metrics, experiments, judge), tests with xUnit v3.

## Run it

```sh
docker compose up -d                       # Qdrant, Ollama (pulls the models) and the API
curl -F file=@handbook.pdf http://localhost:8080/documents
curl -H 'content-type: application/json' -d '{"question":"How many vacation days?"}' http://localhost:8080/ask
```

On a Mac run the Ollama app instead of the container to use the GPU (see
`docker-compose.yml`). The API's defaults are the v0.1 baseline (fixed chunks,
nomic-embed-text); the measured choices above are configurations of the experiments.

From the CLI:

```sh
dotnet run --project src/RagEval.Cli -- ingest ./docs
dotnet run --project src/RagEval.Cli -- ask "How many vacation days?" --rerank qwen3-reranker:0.6b
```

## Reproduce the results

```sh
./scripts/pull-models.sh                   # every model at the digest in models/models.lock
./scripts/experiments/embeddings.sh        # or baseline, chunking, hybrid, rerank, answers, latency
./scripts/eval-smoke.sh                    # fast regression check against results/smoke/
./scripts/check.sh                         # build, format and tests
```

Each experiment script checks Qdrant and the pinned model digests, downloads its datasets
(verified by SHA-256) and writes `results/<name>/` with the commit, machine, Ollama and Qdrant
versions and model digests recorded. Indexes are cached in Qdrant and reused across runs.
There is no CI by choice: the evaluation needs local models, so checks run on the developer's
machine.

## Data and licences

Datasets are downloaded, never committed; see [docs/datasets.md](docs/datasets.md). Results
store ids and numbers only. Turkish RAG Eval is CC BY-SA 4.0 (Turkish Wikipedia text); SciFact is
CC BY 4.0 / ODC-By 1.0 (Wadden et al., 2020). Answer experiments keep generated answers and the
passages they were given per question; files that contain dataset text carry the dataset's
licence.

## License

[MIT](LICENSE) © Mert Türkoğlu · [mertturkoglu.com](https://www.mertturkoglu.com)
