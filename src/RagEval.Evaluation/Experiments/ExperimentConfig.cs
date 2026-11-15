using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagEval.Evaluation.Experiments;

/// <summary>How a run chunks documents.</summary>
public sealed record ChunkingConfig
{
    /// <summary><c>fixed</c>, <c>structural</c> or <c>sentence-window</c>.</summary>
    public string Strategy { get; init; } = "fixed";

    /// <summary>Largest chunk in characters (fixed, structural).</summary>
    public int Size { get; init; } = 1000;

    /// <summary>Characters shared by consecutive chunks (fixed).</summary>
    public int Overlap { get; init; } = 200;

    /// <summary>Sentences of context on each side (sentence-window).</summary>
    public int Window { get; init; } = 2;
}

/// <summary>Which embedding model a run uses.</summary>
public sealed record EmbeddingConfig
{
    public string Model { get; init; } = "nomic-embed-text";
}

/// <summary>How a run retrieves.</summary>
public sealed record RetrievalConfig
{
    /// <summary><c>dense</c>, <c>sparse</c> (BM25) or <c>hybrid</c> (both, fused by reciprocal rank).</summary>
    public string Strategy { get; init; } = "dense";

    /// <summary>Chunks retrieved per query; documents are ranked from these.</summary>
    public int Depth { get; init; } = 100;

    /// <summary>How BM25 cuts text into terms: <c>plain</c> or <c>prefix-N</c> (first N letters of each word).</summary>
    public string Analyzer { get; init; } = "plain";

    /// <summary>BM25 term-frequency saturation.</summary>
    public double K1 { get; init; } = 1.2;

    /// <summary>BM25 length normalisation.</summary>
    public double B { get; init; } = 0.75;

    /// <summary>Reciprocal rank fusion constant (hybrid): larger values flatten the weight of top ranks.</summary>
    public int RrfK { get; init; } = 60;

    /// <summary>Chunks taken from each retriever before fusion (hybrid); at least <see cref="Depth"/>.</summary>
    public int Candidates { get; init; } = 100;
}

/// <summary>One configuration of the pipeline to evaluate.</summary>
public sealed record RunConfig
{
    public required string Name { get; init; }

    public ChunkingConfig Chunking { get; init; } = new();

    public EmbeddingConfig Embedding { get; init; } = new();

    public RetrievalConfig Retrieval { get; init; } = new();
}

/// <summary>An experiment: every run on every dataset. Read from <c>experiments/*.json</c>.</summary>
public sealed record ExperimentConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required IReadOnlyList<string> Datasets { get; init; }

    public required IReadOnlyList<RunConfig> Runs { get; init; }

    public static ExperimentConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<ExperimentConfig>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"Cannot read experiment {path}.");
        if (config.Runs.Count == 0 || config.Datasets.Count == 0)
        {
            throw new InvalidDataException($"Experiment {path} needs at least one dataset and one run.");
        }

        var duplicate = config.Runs.GroupBy(r => r.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Experiment {path}: run name '{duplicate.Key}' is used twice.");
        }

        return config;
    }
}
