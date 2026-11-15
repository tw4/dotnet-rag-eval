using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using RagEval.Core.Answering;
using RagEval.Core.Configuration;
using RagEval.Core.Indexing;
using RagEval.Core.Reranking;
using RagEval.Core.Telemetry;
using RagEval.Evaluation.Datasets;

namespace RagEval.Evaluation.Experiments;

/// <summary>A latency benchmark: each run answers the same questions end to end. Read from <c>experiments/*.json</c>.</summary>
public sealed record LatencyConfig
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

    /// <summary>Dataset whose questions are answered and whose documents are indexed.</summary>
    public required string Dataset { get; init; }

    /// <summary>Questions answered per run (the first N of the dataset).</summary>
    public int Queries { get; init; } = 50;

    /// <summary>Questions answered first and not measured: model loading, caches.</summary>
    public int WarmUp { get; init; } = 3;

    /// <summary>Documents indexed one by one to time ingestion (into a temporary collection).</summary>
    public int Documents { get; init; } = 20;

    public required IReadOnlyList<RunConfig> Runs { get; init; }

    public static LatencyConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<LatencyConfig>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"Cannot read latency benchmark {path}.");
        return config.Runs.Count > 0 ? config : throw new InvalidDataException($"Latency benchmark {path} needs at least one run.");
    }
}

/// <summary>p50, p95 and mean of one stage, in milliseconds (or tokens per second).</summary>
public sealed record StageLatency(string Stage, double P50, double P95, double Mean, int Count);

/// <summary>One run's per-stage latencies.</summary>
public sealed record LatencyRunResult(RunConfig Run, IReadOnlyList<StageLatency> Answer, IReadOnlyList<StageLatency> Ingestion,
    int Refusals, int Answers);

public sealed record LatencyResult(LatencyConfig Benchmark, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, RunEnvironment Environment,
    IReadOnlyList<LatencyRunResult> Runs);

/// <summary>
/// Times every stage of the pipeline with <see cref="StageRecorder"/>: per question, query
/// embedding, retrieval (search and fusion), reranking, generation and the whole answer; per
/// document, chunking, embedding and storing. Retrieval is reported without reranking ("search").
/// Answers are always generated (no relevance gate), so generation is timed for every question.
/// </summary>
public sealed class LatencyBench(QdrantClient qdrant, OllamaOptions ollama, ExperimentRunner runner,
    Func<string, CancellationToken, Task<EvalDataset>> loadDataset)
{
    public Action<string>? Log { get; init; }

    /// <summary>Creates the chat client for a model (tests substitute a fake).</summary>
    public Func<string, IChatClient> ChatFor { get; init; } = model => Clients.Chat(ollama, model);

    /// <summary>Stage names in report order, with their labels.</summary>
    public static readonly IReadOnlyList<(string Stage, string Label)> AnswerStages =
    [
        (Stages.EmbedQuery, "Query embed"),
        ("query.search", "Search"),
        (Stages.Rerank, "Rerank"),
        (Stages.Generate, "Generate"),
        (Stages.Answer, "Total"),
        ("tokens_per_second", "Tokens/s"),
    ];

    public static readonly IReadOnlyList<(string Stage, string Label)> IngestionStages =
    [
        (Stages.Chunk, "Chunk"),
        (Stages.EmbedDocuments, "Embed"),
        (Stages.Store, "Store"),
        (Stages.Document, "Total"),
    ];

    public async Task<LatencyResult> RunAsync(LatencyConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var started = DateTimeOffset.UtcNow;
        var models = config.Runs.SelectMany(r => new[] { r.Embedding.Model, r.Answering.ChatModel, r.Rerank?.Model })
            .OfType<string>().Distinct();
        var environment = await RunEnvironmentProbe.CollectAsync(qdrant, ollama, models, cancellationToken);
        var dataset = await loadDataset(config.Dataset, cancellationToken);
        var results = new List<LatencyRunResult>();
        foreach (var run in config.Runs)
        {
            Log?.Invoke($"{config.Dataset} / {run.Name}");
            results.Add(await RunOneAsync(config, dataset, run, cancellationToken));
        }

        return new LatencyResult(config, started, DateTimeOffset.UtcNow, environment, results);
    }

    private async Task<LatencyRunResult> RunOneAsync(LatencyConfig config, EvalDataset dataset, RunConfig run, CancellationToken cancellationToken)
    {
        using var prepared = await runner.PrepareAsync(dataset, run, cancellationToken);
        var retriever = prepared.Reranker is { } reranker
            ? new RerankingRetriever(prepared.Retriever, reranker, run.Rerank!.Candidates)
            : prepared.Retriever;
        using var chat = ChatFor(run.Answering.ChatModel);
        var answerer = new Answerer(prepared.Embedder, retriever, chat,
            new AnsweringOptions { TopK = run.Answering.TopK, MinScore = double.NegativeInfinity }, Clients.DeterministicChatOptions());

        using var recorder = new StageRecorder();
        var questions = dataset.Queries.Take(config.WarmUp + config.Queries).ToList();
        int refusals = 0, answers = 0;
        for (var i = 0; i < questions.Count; i++)
        {
            if (i == config.WarmUp)
            {
                recorder.Clear();
            }

            var answer = await answerer.AskAsync(questions[i].Text, cancellationToken);
            if (i >= config.WarmUp)
            {
                answers++;
                refusals += answer.Refused ? 1 : 0;
            }

            if ((i + 1) % 10 == 0)
            {
                Log?.Invoke($"  {i + 1}/{questions.Count} questions");
            }
        }

        var perAnswer = recorder.Requests(Stages.Answer).Select(WithDerived).ToList();
        var ingestion = prepared.Embedder is null ? [] : await TimeIngestionAsync(config, dataset, prepared, cancellationToken);
        return new LatencyRunResult(run, Summarize(perAnswer, AnswerStages), Summarize(ingestion, IngestionStages), refusals, answers);
    }

    /// <summary>Search = retrieval without reranking; tokens per second of generation.</summary>
    internal static IReadOnlyDictionary<string, double> WithDerived(IReadOnlyDictionary<string, double> stages)
    {
        var result = new Dictionary<string, double>(stages, StringComparer.Ordinal);
        if (stages.TryGetValue(Stages.Retrieve, out var retrieve))
        {
            result["query.search"] = retrieve - stages.GetValueOrDefault(Stages.Rerank);
        }

        if (stages.TryGetValue(Stages.Generate, out var generate) && generate > 0 &&
            stages.TryGetValue(Stages.Generate + StageRecorder.TokensSuffix, out var tokens) && tokens > 0)
        {
            result["tokens_per_second"] = tokens / (generate / 1000);
        }

        return result;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, double>>> TimeIngestionAsync(LatencyConfig config, EvalDataset dataset,
        PreparedRun prepared, CancellationToken cancellationToken)
    {
        var collection = $"bench-{Guid.NewGuid():N}"[..20];
        using var recorder = new StageRecorder();
        try
        {
            var indexer = new Indexer(prepared.Chunker, prepared.Embedder!, new ChunkStore(qdrant, collection)) { Recreate = true };
            var documents = dataset.Documents().Take(config.Documents + 1).ToList();
            for (var i = 0; i < documents.Count; i++)
            {
                if (i == 1)
                {
                    recorder.Clear();   // the first document creates the collection
                }

                await indexer.IndexAsync(documents[i], cancellationToken);
            }

            Log?.Invoke($"  indexed {documents.Count - 1} documents one by one");
            return recorder.Requests(Stages.Document);
        }
        finally
        {
            if (await qdrant.CollectionExistsAsync(collection, CancellationToken.None))
            {
                await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
            }
        }
    }

    internal static IReadOnlyList<StageLatency> Summarize(IReadOnlyList<IReadOnlyDictionary<string, double>> requests,
        IReadOnlyList<(string Stage, string Label)> stages) =>
        [.. stages
            .Select(s => (s.Stage, Values: requests.Where(r => r.ContainsKey(s.Stage)).Select(r => r[s.Stage]).ToList()))
            .Where(s => s.Values.Count > 0)
            .Select(s => new StageLatency(s.Stage, Statistics.Percentile(s.Values, 50), Statistics.Percentile(s.Values, 95), s.Values.Average(),
                s.Values.Count))];

    public static (string Json, string Markdown) Write(LatencyResult result, string folder)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(folder);
        var json = Path.Combine(folder, "results.json");
        var markdown = Path.Combine(folder, "summary.md");
        File.WriteAllText(json, JsonSerializer.Serialize(result, ReportWriter.JsonOptions));
        File.WriteAllText(markdown, Markdown(result));
        return (json, markdown);
    }

    public static string Markdown(LatencyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var inv = CultureInfo.InvariantCulture;
        var env = result.Environment;
        var b = result.Benchmark;
        var sb = new StringBuilder();
        sb.Append(inv, $"# {b.Name}\n\n");
        if (b.Description.Length > 0)
        {
            sb.Append(b.Description).Append("\n\n");
        }

        sb.Append(inv, $"Run {result.StartedAt:yyyy-MM-dd HH:mm} UTC, {(result.FinishedAt - result.StartedAt).TotalMinutes:F1} min, on {env.Cpu}");
        sb.Append(inv, $" · commit `{env.GitCommit?[..Math.Min(7, env.GitCommit.Length)] ?? "?"}`{(env.GitDirty == true ? " (dirty)" : "")}");
        sb.Append(inv, $" · Ollama {env.OllamaVersion ?? "?"} · Qdrant {env.QdrantVersion ?? "?"}\n");
        sb.Append(inv, $"{b.Dataset}: {b.Queries} questions per run after {b.WarmUp} warm-up questions; {b.Documents} documents indexed one by one.\n");

        sb.Append("\n## Answering a question (ms)\n\n| Run |");
        foreach (var (_, label) in AnswerStages)
        {
            sb.Append(inv, $" {label} p50 / p95 |");
        }

        sb.Append("\n|---|").Append(string.Concat(AnswerStages.Select(_ => "---:|"))).Append('\n');
        foreach (var run in result.Runs)
        {
            sb.Append(inv, $"| {run.Run.Name} |");
            foreach (var (stage, _) in AnswerStages)
            {
                sb.Append(Cell(run.Answer, stage, stage == "tokens_per_second" ? "F1" : "F0"));
            }

            sb.Append('\n');
        }

        sb.Append("\n## Indexing a document (ms)\n\n| Run |");
        foreach (var (_, label) in IngestionStages)
        {
            sb.Append(inv, $" {label} p50 / p95 |");
        }

        sb.Append("\n|---|").Append(string.Concat(IngestionStages.Select(_ => "---:|"))).Append('\n');
        foreach (var run in result.Runs.Where(r => r.Ingestion.Count > 0))
        {
            sb.Append(inv, $"| {run.Run.Name} |");
            foreach (var (stage, _) in IngestionStages)
            {
                sb.Append(Cell(run.Ingestion, stage, stage == Stages.Chunk ? "F1" : "F0"));
            }

            sb.Append('\n');
        }

        sb.Append("\nSearch: retrieval without reranking (dense and BM25 searches, fusion). Total: from the question to the parsed answer. ")
          .Append("Tokens/s: output tokens over generation time, including prompt processing. ")
          .Append("Indexing: chunking, embedding every chunk, and replacing the document's points in Qdrant.\n");
        return sb.ToString();

        static string Cell(IReadOnlyList<StageLatency> stages, string stage, string format)
        {
            var s = stages.FirstOrDefault(x => x.Stage == stage);
            return s is null ? " – |" : string.Create(CultureInfo.InvariantCulture, $" {s.P50.ToString(format, CultureInfo.InvariantCulture)} / {s.P95.ToString(format, CultureInfo.InvariantCulture)} |");
        }
    }
}
