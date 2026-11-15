using System.Diagnostics;
using Qdrant.Client;
using RagEval.Core.Chunking;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
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

    public async Task<ExperimentResult> RunAsync(ExperimentConfig experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        var started = DateTimeOffset.UtcNow;
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

        var environment = await RunEnvironmentProbe.CollectAsync(qdrant, ollama,
            experiment.Runs.Select(r => r.Embedding.Model).Distinct(), cancellationToken);
        return new ExperimentResult(experiment, started, DateTimeOffset.UtcNow, environment, results, QueryLimit);
    }

    public static IChunker CreateChunker(ChunkingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Strategy switch
        {
            "fixed" => new FixedSizeChunker(config.Size, config.Overlap),
            _ => throw new NotSupportedException($"Unknown chunking strategy '{config.Strategy}'."),
        };
    }

    public static IRetriever CreateRetriever(RetrievalConfig config, ChunkStore store)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Strategy switch
        {
            "dense" => new DenseRetriever(store),
            _ => throw new NotSupportedException($"Unknown retrieval strategy '{config.Strategy}'."),
        };
    }

    private async Task<RunResult> RunOneAsync(EvalDataset dataset, RunConfig run, CancellationToken cancellationToken)
    {
        using var generator = EmbeddingsFor(run.Embedding.Model);
        var embedder = new Embedder(generator, EmbeddingProfile.For(run.Embedding.Model));
        var chunker = CreateChunker(run.Chunking);
        var index = await new CorpusIndex(qdrant, chunker, embedder).EnsureAsync(dataset, log: Log, cancellationToken: cancellationToken);
        Log?.Invoke(index.Reused ? $"  index reused: {index.Collection}" : $"  indexed {index.Chunks} chunks in {index.Elapsed.TotalSeconds:F0} s");

        var retriever = CreateRetriever(run.Retrieval, new ChunkStore(qdrant, index.Collection));

        var queries = QueryLimit is { } limit ? dataset.Queries.Take(limit).ToList() : dataset.Queries;
        var perQuery = new List<QueryResult>(queries.Count);
        var watch = new Stopwatch();
        foreach (var query in queries)
        {
            watch.Restart();
            var vector = await embedder.EmbedQueryAsync(query.Text, cancellationToken);
            var embedMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var chunks = await retriever.RetrieveAsync(query.Text, vector, run.Retrieval.Depth, cancellationToken);
            var searchMs = watch.Elapsed.TotalMilliseconds;

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
                searchMs));
        }

        return Summarize(dataset.Info.Id, run, index, perQuery);
    }

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
            Index = new IndexSummary(index.Collection, index.Documents, index.Chunks, index.Characters, index.Elapsed.TotalSeconds, index.Reused),
            Queries = perQuery.Count,
            Metrics = metrics,
            Ndcg10Ci95 = [ci.Low, ci.High],
            QueryEmbedding = Lat(q => q.EmbedMs),
            Search = Lat(q => q.SearchMs),
            PerQuery = perQuery,
        };
    }
}
