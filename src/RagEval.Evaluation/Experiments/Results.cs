namespace RagEval.Evaluation.Experiments;

/// <summary>Metrics of one query.</summary>
public sealed record QueryResult(string QueryId, double Ndcg10, double Recall10, double Recall100, double Mrr10,
    bool? AnswerHit5, bool? AnswerHit10, IReadOnlyList<string> Top10Documents, double EmbedMs, double SearchMs);

/// <summary>How the corpus was indexed for a run.</summary>
/// <param name="Collection">Qdrant collection.</param>
/// <param name="Documents">Documents indexed.</param>
/// <param name="Chunks">Chunks (= vectors) stored.</param>
/// <param name="Characters">Characters of chunk text stored.</param>
/// <param name="Seconds">Time it took to build the index (also when reused: from when it was built).</param>
/// <param name="Reused">The index existed and was reused by this run.</param>
/// <param name="Dimensions">Embedding size.</param>
/// <param name="VectorBytes">Raw float32 vector storage: chunks × dimensions × 4.</param>
public sealed record IndexSummary(string Collection, int Documents, int Chunks, long Characters, double Seconds, bool Reused,
    int Dimensions, long VectorBytes);

/// <summary>Latency percentiles, in milliseconds.</summary>
public sealed record Latency(double P50, double P95);

/// <summary>One run on one dataset: aggregate metrics and every query's result.</summary>
public sealed record RunResult
{
    public required string Dataset { get; init; }
    public required RunConfig Run { get; init; }
    public required IndexSummary Index { get; init; }
    public required int Queries { get; init; }

    /// <summary>Mean over queries: ndcg@10, recall@10, recall@100, mrr@10, answer_hit@5, answer_hit@10.</summary>
    public required IReadOnlyDictionary<string, double> Metrics { get; init; }

    /// <summary>95% bootstrap confidence interval of the mean nDCG@10.</summary>
    public required double[] Ndcg10Ci95 { get; init; }

    public required Latency QueryEmbedding { get; init; }
    public required Latency Search { get; init; }
    public required IReadOnlyList<QueryResult> PerQuery { get; init; }
}

/// <summary>Where and with what the experiment ran, for reproducibility.</summary>
public sealed record RunEnvironment(string? GitCommit, bool? GitDirty, string Os, string Cpu, string DotNet, string? OllamaVersion,
    string? QdrantVersion, IReadOnlyDictionary<string, string> ModelDigests);

/// <summary>The raw results of an experiment, written as results.json.</summary>
public sealed record ExperimentResult(ExperimentConfig Experiment, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    RunEnvironment Environment, IReadOnlyList<RunResult> Runs, int? QueryLimit);
