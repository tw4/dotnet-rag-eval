# dotnet-rag-eval

**On-premises RAG in .NET, with every stage measured.**

> **Status: planning.** There is no code yet. This README describes what the project
> will be; the work is tracked in [ROADMAP.md](ROADMAP.md), the
> [milestones](../../milestones) and the [issues](../../issues).

Most retrieval-augmented generation examples stop at "it answered my question". This one
is a reference implementation for .NET teams that need to keep documents in-house, and it
answers the questions that come after the demo: which chunking works for these documents,
which embedding model, does hybrid search help, is the reranker worth its latency, and is
the answer actually supported by the retrieved text.

```
 documents ──► ingest ──► chunk ──► embed ──► Qdrant (dense + sparse)
                                                   │
 question ──► embed ──► hybrid search ──► rerank ──┴──► answer with citations
                                                              │
                                    evaluation: retrieval metrics, faithfulness, latency
```

Everything runs locally: models through Ollama, vectors in Qdrant, the app in .NET.

## Planned features

| Stage | Options to compare |
|---|---|
| **Ingestion** | Markdown, HTML, PDF text extraction with source positions |
| **Chunking** | Fixed size, recursive by structure, sentence-window |
| **Embeddings** | Several local embedding models, including multilingual ones for Turkish |
| **Retrieval** | Dense, sparse (BM25-style), hybrid with fusion |
| **Reranking** | None vs. a local reranker |
| **Generation** | Answers with citations to chunk and source position |

## Measured, not claimed

The evaluation is the product. The repository will include:

- **Retrieval metrics** on a public dataset: recall@k, MRR, nDCG@10
- **Answer evaluation:** faithfulness to the retrieved context and correctness, scored by
  a judge model that is first checked against a hand-labelled sample
- **Latency:** p50/p95 per stage
- **Experiments:** one table per decision (chunking, embedding model, hybrid, reranking),
  reproducible with one command

Results will be published in this README, including the configurations that did not help.

## Roadmap at a glance

| Milestone | Outcome |
|---|---|
| **v0.1 — Baseline pipeline** | Ingest, chunk, embed, search and answer with citations, `docker compose up` |
| **v0.2 — Retrieval evaluation** | Dataset loader, retrieval metrics, `eval` command, result reports |
| **v0.3 — Experiments** | Chunking, embedding models, hybrid search and reranking compared |
| **v0.4 — Answer evaluation** | Faithfulness and correctness judge, calibrated on labelled data, latency |
| **v1.0 — Report** | Write-up of the results, reproducible scripts, CI on a small eval subset |

Details: [ROADMAP.md](ROADMAP.md).

## Planned stack

C# on .NET 10, ASP.NET Core minimal APIs, Qdrant, Ollama, `Microsoft.Extensions.AI`,
xUnit, Docker Compose.

## License

[MIT](LICENSE) © Mert Türkoğlu · [mertturkoglu.com](https://www.mertturkoglu.com)
