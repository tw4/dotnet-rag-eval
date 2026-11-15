using System.Diagnostics;
using Qdrant.Client;
using RagEval.Core.Chunking;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Core.Reranking;
using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Metrics;

namespace RagEval.Evaluation.Experiments;

/// <summary>Runs every run of an experiment on every dataset and collects the results.</summary>
public sealed class ExperimentRunner(QdrantClient qdrant, OllamaOptions ollama, Func<string, CancellationToken, Task<EvalDataset>> loadDataset)
{
    /// <summary>Evaluate only the first N queries of each dataset (quick checks). Null: all.</summary>
    public int? QueryLimit { get; init; }

    /// <summary>Progress messages.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Creates the embedding generator for a model (tests substitute a fake).</summary>
    public Func<string, Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>> EmbeddingsFor { get; init; } =
        model => Clients.Embeddings(ollama, model);

    /// <summary>Creates the reranker of a run (tests substitute a fake).</summary>
    public Func<RerankConfig, IReranker> RerankerFor { get; init; } =
        config => new YesNoReranker(new OllamaSharp.OllamaApiClient(ollama.Url), config.Model, config.Parallelism);

    public async Task<ExperimentResult> RunAsync(ExperimentConfig experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        var started = DateTimeOffset.UtcNow;

        // Collected first, so the commit and model digests are those of the code and models that ran.
        var environment = await RunEnvironmentProbe.CollectAsync(qdrant, ollama,
            experiment.Runs.Where(r => UsesDense(r.Retrieval)).Select(r => r.Embedding.Model)
                .Concat(experiment.Runs.Where(r => r.Rerank is not null).Select(r => r.Rerank!.Model)).Distinct(), cancellationToken);
        var results = new List<RunResult>();
        foreach (var datasetId in experiment.Datasets)
        {
            var dataset = await loadDataset(datasetId, cancellationToken);
            foreach (var run in experiment.Runs)
            {
                Log?.Invoke($"{datasetId} / {run.Name}");
                results.Add(await RunOneAsync(dataset, run, cancellationToken));
            }
        }

        return new ExperimentResult(experiment, started, DateTimeOffset.UtcNow, environment, results, QueryLimit);
    }

    public static IChunker CreateChunker(ChunkingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Strategy switch
        {
            "fixed" => new FixedSizeChunker(config.Size, config.Overlap),
            "structural" => new StructuralChunker(config.Size),
            "sentence-window" => new SentenceWindowChunker(config.Window),
            _ => throw new NotSupportedException($"Unknown chunking strategy '{config.Strategy}'."),
        };
    }

    private static bool UsesDense(RetrievalConfig config) => config.Strategy is "dense" or "hybrid";

    private static bool UsesSparse(RetrievalConfig config) => config.Strategy is "sparse" or "hybrid";

    /// <summary>The retriever for a configuration, over a dense and/or a sparse (BM25) index of the same chunks.</summary>
    public static IRetriever CreateRetriever(RetrievalConfig config, ChunkStore? dense, (ChunkStore Store, Bm25Encoder Encoder)? sparse)
    {
        ArgumentNullException.ThrowIfNull(config);
        IRetriever Dense() => new DenseRetriever(dense ?? throw new ArgumentNullException(nameof(dense)));
        IRetriever Sparse() => sparse is { } s ? new SparseRetriever(s.Store, s.Encoder) : throw new ArgumentNullException(nameof(sparse));
        return config.Strategy switch
        {
            "dense" => Dense(),
            "sparse" => Sparse(),
            "hybrid" => new HybridRetriever(Dense(), Sparse(), config.RrfK, config.Candidates),
            _ => throw new NotSupportedException($"Unknown retrieval strategy '{config.Strategy}'."),
        };
    }

    private async Task<RunResult> RunOneAsync(EvalDataset dataset, RunConfig run, CancellationToken cancellationToken)
    {
        var retrieval = run.Retrieval;
        if (!UsesDense(retrieval) && !UsesSparse(retrieval))
        {
            throw new NotSupportedException($"Unknown retrieval strategy '{retrieval.Strategy}'.");
        }

        var chunker = CreateChunker(run.Chunking);
        using var generator = UsesDense(retrieval) ? EmbeddingsFor(run.Embedding.Model) : null;
        var embedder = generator is null ? null : new Embedder(generator, EmbeddingProfile.For(run.Embedding.Model));

        IndexResult? denseIndex = null, sparseIndex = null;
        (ChunkStore, Bm25Encoder)? sparse = null;
        if (embedder is not null)
        {
            denseIndex = await new CorpusIndex(qdrant, chunker, embedder).EnsureAsync(dataset, log: Log, cancellationToken: cancellationToken);
            LogIndex(denseIndex);
        }

        if (UsesSparse(retrieval))
        {
            var (built, encoder) = await new SparseCorpusIndex(qdrant, chunker, TextAnalyzer.Parse(retrieval.Analyzer), retrieval.K1, retrieval.B)
                .EnsureAsync(dataset, cancellationToken: cancellationToken);
            LogIndex(built);
            sparseIndex = built;
            sparse = (new ChunkStore(qdrant, built.Collection), encoder);
        }

        var retriever = CreateRetriever(retrieval, denseIndex is null ? null : new ChunkStore(qdrant, denseIndex.Collection), sparse);
        var index = Combine(denseIndex, sparseIndex);
        var reranker = run.Rerank is null ? null : RerankerFor(run.Rerank);
        var depth = Math.Max(retrieval.Depth, run.Rerank?.Candidates ?? 0);

        var queries = QueryLimit is { } limit ? dataset.Queries.Take(limit).ToList() : dataset.Queries;
        var perQuery = new List<QueryResult>(queries.Count);
        var watch = new Stopwatch();
        foreach (var query in queries)
        {
            watch.Restart();
            var vector = embedder is null ? ReadOnlyMemory<float>.Empty : await embedder.EmbedQueryAsync(query.Text, cancellationToken);
            var embedMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var chunks = await retriever.RetrieveAsync(query.Text, vector, depth, cancellationToken);
            var searchMs = watch.Elapsed.TotalMilliseconds;
            var rerankMs = 0.0;
            if (reranker is not null)
            {
                watch.Restart();
                var candidates = run.Rerank!.Candidates;
                var scores = await reranker.ScoreAsync(query.Text, [.. chunks.Take(candidates).Select(Rerank.Passage)], cancellationToken);
                chunks = [.. Rerank.Apply(chunks, scores, candidates).Take(retrieval.Depth)];
                rerankMs = watch.Elapsed.TotalMilliseconds;
            }

            var judgements = dataset.Qrels[query.Id];
            var ranking = RetrievalMetrics.DocumentRanking(chunks);
            perQuery.Add(new QueryResult(
                query.Id,
                RetrievalMetrics.NdcgAt(ranking, judgements, 10),
                RetrievalMetrics.RecallAt(ranking, judgements, 10),
                RetrievalMetrics.RecallAt(ranking, judgements, 100),
                RetrievalMetrics.ReciprocalRankAt(ranking, judgements, 10),
                query.AnswerSpan is null ? null : RetrievalMetrics.AnswerHitAt(chunks, judgements, query.AnswerSpan, 5),
                query.AnswerSpan is null ? null : RetrievalMetrics.AnswerHitAt(chunks, judgements, query.AnswerSpan, 10),
                [.. ranking.Take(10)],
                embedMs,
                searchMs,
                rerankMs));
        }

        return Summarize(dataset.Info.Id, run, index, perQuery);
    }

    private void LogIndex(IndexResult index) =>
        Log?.Invoke(index.Reused ? $"  index reused: {index.Collection}" : $"  indexed {index.Chunks} chunks in {index.Elapsed.TotalSeconds:F0} s");

    /// <summary>One index, or both indexes of a hybrid run (same chunks): times and vector sizes add up.</summary>
    internal static IndexResult Combine(IndexResult? dense, IndexResult? sparse) => (dense, sparse) switch
    {
        ({ } d, null) => d,
        (null, { } s) => s,
        ({ } d, { } s) => d with
        {
            Collection = $"{d.Collection} + {s.Collection}",
            Elapsed = d.Elapsed + s.Elapsed,
            Reused = d.Reused && s.Reused,
            SparseBytes = d.VectorBytes + s.VectorBytes,
        },
        _ => throw new ArgumentException("A run needs at least one index."),
    };

    internal static RunResult Summarize(string dataset, RunConfig run, IndexResult index, IReadOnlyList<QueryResult> perQuery)
    {
        double Mean(Func<QueryResult, double> f) => perQuery.Count == 0 ? double.NaN : perQuery.Average(f);
        var metrics = new Dictionary<string, double>
        {
            ["ndcg@10"] = Mean(q => q.Ndcg10),
            ["recall@10"] = Mean(q => q.Recall10),
            ["recall@100"] = Mean(q => q.Recall100),
            ["mrr@10"] = Mean(q => q.Mrr10),
        };
        var withSpans = perQuery.Where(q => q.AnswerHit5 is not null).ToList();
        if (withSpans.Count > 0)
        {
            metrics["answer_hit@5"] = withSpans.Average(q => q.AnswerHit5!.Value ? 1.0 : 0.0);
            metrics["answer_hit@10"] = withSpans.Average(q => q.AnswerHit10!.Value ? 1.0 : 0.0);
        }

        var ci = Statistics.BootstrapCi95([.. perQuery.Select(q => q.Ndcg10)]);
        Latency Lat(Func<QueryResult, double> f)
        {
            var values = perQuery.Select(f).ToList();
            return new Latency(Statistics.Percentile(values, 50), Statistics.Percentile(values, 95));
        }

        return new RunResult
        {
            Dataset = dataset,
            Run = run,
            Index = new IndexSummary(index.Collection, index.Documents, index.Chunks, index.Characters, index.Elapsed.TotalSeconds,
                index.Reused, index.Dimensions, index.VectorBytes),
            Queries = perQuery.Count,
            Metrics = metrics,
            Ndcg10Ci95 = [ci.Low, ci.High],
            QueryEmbedding = Lat(q => q.EmbedMs),
            Search = Lat(q => q.SearchMs),
            Rerank = run.Rerank is null ? null : Lat(q => q.RerankMs),
            PerQuery = perQuery,
        };
    }
}
