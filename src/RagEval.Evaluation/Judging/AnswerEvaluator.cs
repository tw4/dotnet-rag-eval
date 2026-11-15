using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using RagEval.Core.Answering;
using RagEval.Core.Configuration;
using RagEval.Core.Reranking;
using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Experiments;
using RagEval.Evaluation.Metrics;

namespace RagEval.Evaluation.Judging;

/// <summary>One answered question and how it was judged.</summary>
/// <param name="QueryId">Dataset query id.</param>
/// <param name="Question">The question.</param>
/// <param name="Answer">The generated answer, with its [n] markers.</param>
/// <param name="Refused">The model (or the pipeline) declined to answer.</param>
/// <param name="RefusalReason">Why, for refusals.</param>
/// <param name="Reference">The dataset's answer span, which correctness is judged against.</param>
/// <param name="ContextChunks">Ids of the chunks given to the model, in order.</param>
/// <param name="Context">The passages given to the model, as the judge read them.</param>
/// <param name="Cited">Numbers of the passages the answer cites.</param>
/// <param name="ContextHasAnswer">A chunk given to the model comes from a relevant document and contains the answer span.</param>
/// <param name="CitedHasAnswer">A cited chunk contains the answer span (null: nothing cited).</param>
/// <param name="Faithfulness">Null for refusals: there is nothing to support.</param>
/// <param name="Correctness">Refusals are not asked: every question has an answer, so they count as "no".</param>
/// <param name="AnswerMs">Time to retrieve and generate the answer.</param>
/// <param name="JudgeMs">Time the judge took for both verdicts.</param>
public sealed record AnswerQueryResult(string QueryId, string Question, string Answer, bool Refused, string? RefusalReason,
    string Reference, IReadOnlyList<string> ContextChunks, IReadOnlyList<string> Context, IReadOnlyList<int> Cited, bool ContextHasAnswer, bool? CitedHasAnswer,
    JudgeVerdict? Faithfulness, JudgeVerdict? Correctness, double AnswerMs, double JudgeMs);

/// <summary>One run's answers on one dataset.</summary>
public sealed record AnswerRunResult
{
    public required string Dataset { get; init; }
    public required RunConfig Run { get; init; }
    public required int Queries { get; init; }

    /// <summary>answered, faithfulness, correctness, correctness_given_context, context_has_answer, cited, cited_has_answer, refused_without_context.</summary>
    public required IReadOnlyDictionary<string, double> Metrics { get; init; }

    /// <summary>95% bootstrap interval of the mean correctness.</summary>
    public required double[] CorrectnessCi95 { get; init; }

    public required Latency Answer { get; init; }
    public required IReadOnlyList<AnswerQueryResult> PerQuery { get; init; }
}

public sealed record AnswerExperimentResult(ExperimentConfig Experiment, string JudgeModel, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    RunEnvironment Environment, IReadOnlyList<AnswerRunResult> Runs, int? QueryLimit);

/// <summary>
/// Generates an answer for every question of datasets with answer spans, then judges it for
/// faithfulness to the passages it was written from and for correctness against the answer
/// span. Answers are always generated (no relevance gate), so the model's own refusals are
/// measured: a refusal is right when the passages lack the answer, and wrong otherwise.
/// </summary>
public sealed class AnswerEvaluator(QdrantClient qdrant, OllamaOptions ollama, ExperimentRunner runner,
    Func<string, CancellationToken, Task<EvalDataset>> loadDataset)
{
    public int? QueryLimit { get; init; }

    public Action<string>? Log { get; init; }

    /// <summary>Creates a chat client for a model, for answers and for the judge (tests substitute fakes).</summary>
    public Func<string, IChatClient> ChatFor { get; init; } = model => Clients.Chat(ollama, model);

    public async Task<AnswerExperimentResult> RunAsync(ExperimentConfig experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        var judgeModel = experiment.Judge?.Model ?? throw new InvalidDataException($"Experiment '{experiment.Name}' has no judge.");
        var started = DateTimeOffset.UtcNow;
        var models = experiment.Runs.SelectMany(r => new[] { r.Embedding.Model, r.Answering.ChatModel, r.Rerank?.Model })
            .Append(judgeModel).OfType<string>().Distinct();
        var environment = await RunEnvironmentProbe.CollectAsync(qdrant, ollama, models, cancellationToken);
        using var judgeClient = ChatFor(judgeModel);
        var judge = new AnswerJudge(judgeClient, Clients.DeterministicChatOptions());

        var results = new List<AnswerRunResult>();
        foreach (var datasetId in experiment.Datasets)
        {
            var dataset = await loadDataset(datasetId, cancellationToken);
            if (dataset.Queries.All(q => q.AnswerSpan is null))
            {
                throw new InvalidDataException($"Dataset '{datasetId}' has no answer spans to judge correctness against.");
            }

            foreach (var run in experiment.Runs)
            {
                Log?.Invoke($"{datasetId} / {run.Name}");
                results.Add(await RunOneAsync(dataset, run, judge, cancellationToken));
            }
        }

        return new AnswerExperimentResult(experiment, judgeModel, started, DateTimeOffset.UtcNow, environment, results, QueryLimit);
    }

    private async Task<AnswerRunResult> RunOneAsync(EvalDataset dataset, RunConfig run, AnswerJudge judge, CancellationToken cancellationToken)
    {
        using var prepared = await runner.PrepareAsync(dataset, run, cancellationToken);
        var retriever = prepared.Reranker is { } reranker
            ? new RerankingRetriever(prepared.Retriever, reranker, run.Rerank!.Candidates)
            : prepared.Retriever;
        using var chat = ChatFor(run.Answering.ChatModel);
        var answerer = new Answerer(prepared.Embedder, retriever, chat,
            new AnsweringOptions { TopK = run.Answering.TopK, MinScore = double.NegativeInfinity }, Clients.DeterministicChatOptions());

        var queries = dataset.Queries.Where(q => q.AnswerSpan is not null).ToList();
        if (QueryLimit is { } limit)
        {
            queries = [.. queries.Take(limit)];
        }

        var perQuery = new List<AnswerQueryResult>(queries.Count);
        var watch = new Stopwatch();
        foreach (var query in queries)
        {
            watch.Restart();
            var answer = await answerer.AskAsync(query.Text, cancellationToken);
            var answerMs = watch.Elapsed.TotalMilliseconds;

            var judgements = dataset.Qrels[query.Id];
            var span = query.AnswerSpan!;
            var contextHasAnswer = RetrievalMetrics.AnswerHitAt(answer.Retrieved, judgements, span, answer.Retrieved.Count);
            bool? citedHasAnswer = answer.Citations.Count == 0 ? null
                : answer.Citations.Any(c => RetrievalMetrics.Normalize(c.Text).Contains(RetrievalMetrics.Normalize(span), StringComparison.Ordinal));

            watch.Restart();
            JudgeVerdict? faithfulness = null, correctness;
            var passages = answer.Retrieved.Select(Rerank.Passage).ToList();
            if (answer.Refused)
            {
                correctness = new JudgeVerdict(Verdict.No, "Refused, but the question has an answer.");
            }
            else
            {
                faithfulness = await judge.FaithfulnessAsync(query.Text, answer.Text, passages, cancellationToken);
                correctness = await judge.CorrectnessAsync(query.Text, answer.Text, span, cancellationToken);
            }

            perQuery.Add(new AnswerQueryResult(query.Id, query.Text, answer.Text, answer.Refused, answer.RefusalReason, span,
                [.. answer.Retrieved.Select(c => c.Chunk.Id)], passages, [.. answer.Citations.Select(c => c.Number)], contextHasAnswer, citedHasAnswer,
                faithfulness, correctness, answerMs, watch.Elapsed.TotalMilliseconds));
            if (perQuery.Count % 10 == 0)
            {
                Log?.Invoke($"  {perQuery.Count}/{queries.Count} answered and judged");
            }
        }

        return Summarize(dataset.Info.Id, run, perQuery);
    }

    internal static AnswerRunResult Summarize(string dataset, RunConfig run, IReadOnlyList<AnswerQueryResult> perQuery)
    {
        static double Rate<T>(IReadOnlyCollection<T> items, Func<T, bool> predicate) => items.Count == 0 ? double.NaN : items.Count(predicate) / (double)items.Count;
        var answered = perQuery.Where(q => !q.Refused).ToList();
        var withContext = perQuery.Where(q => q.ContextHasAnswer).ToList();
        var withoutContext = perQuery.Where(q => !q.ContextHasAnswer).ToList();
        var correctness = perQuery.Select(q => q.Correctness?.Score ?? 0).ToArray();
        var metrics = new Dictionary<string, double>
        {
            ["answered"] = Rate(perQuery, q => !q.Refused),
            ["faithfulness"] = answered.Count == 0 ? double.NaN : answered.Average(q => q.Faithfulness!.Score),
            ["correctness"] = correctness.Length == 0 ? double.NaN : correctness.Average(),
            ["context_has_answer"] = Rate(perQuery, q => q.ContextHasAnswer),
            ["correctness_given_context"] = withContext.Count == 0 ? double.NaN : withContext.Average(q => q.Correctness?.Score ?? 0),
            ["refused_without_context"] = Rate(withoutContext, q => q.Refused),
            ["cited"] = Rate(answered, q => q.Cited.Count > 0),
            ["cited_has_answer"] = Rate(answered, q => q.CitedHasAnswer == true),
        };
        var ci = Statistics.BootstrapCi95(correctness);
        var latency = perQuery.Select(q => q.AnswerMs).ToList();
        return new AnswerRunResult
        {
            Dataset = dataset,
            Run = run,
            Queries = perQuery.Count,
            Metrics = metrics,
            CorrectnessCi95 = [ci.Low, ci.High],
            Answer = latency.Count == 0 ? new Latency(double.NaN, double.NaN) : new Latency(Statistics.Percentile(latency, 50), Statistics.Percentile(latency, 95)),
            PerQuery = perQuery,
        };
    }

    public static (string Json, string Markdown) Write(AnswerExperimentResult result, string folder)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(folder);
        var json = Path.Combine(folder, "results.json");
        var markdown = Path.Combine(folder, "summary.md");
        File.WriteAllText(json, JsonSerializer.Serialize(result, ReportWriter.JsonOptions));
        File.WriteAllText(markdown, Markdown(result));
        return (json, markdown);
    }

    public static AnswerExperimentResult Read(string path) =>
        JsonSerializer.Deserialize<AnswerExperimentResult>(File.ReadAllText(path), ReportWriter.JsonOptions)
        ?? throw new InvalidDataException($"Cannot read {path}.");

    public static string Markdown(AnswerExperimentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var inv = CultureInfo.InvariantCulture;
        var env = result.Environment;
        var sb = new StringBuilder();
        sb.Append(inv, $"# {result.Experiment.Name}\n\n");
        if (result.Experiment.Description.Length > 0)
        {
            sb.Append(result.Experiment.Description).Append("\n\n");
        }

        sb.Append(inv, $"Run {result.StartedAt:yyyy-MM-dd HH:mm} UTC, {(result.FinishedAt - result.StartedAt).TotalMinutes:F1} min, on {env.Cpu}");
        sb.Append(inv, $" · commit `{env.GitCommit?[..Math.Min(7, env.GitCommit.Length)] ?? "?"}`{(env.GitDirty == true ? " (dirty)" : "")}");
        sb.Append(inv, $" · Ollama {env.OllamaVersion ?? "?"} · Qdrant {env.QdrantVersion ?? "?"}\n");
        sb.Append(inv, $"Judge: `{result.JudgeModel}`{(env.ModelDigests.TryGetValue(result.JudgeModel, out var d) ? $" ({d})" : "")}\n");
        if (result.QueryLimit is { } limit)
        {
            sb.Append(inv, $"\n> Quick run: only the first {limit} questions of each dataset. Not comparable with full runs.\n");
        }

        foreach (var group in result.Runs.GroupBy(r => r.Dataset))
        {
            var runs = group.ToList();
            sb.Append(inv, $"\n## {group.Key} ({runs[0].Queries} questions)\n\n");
            sb.Append("| Run | Correctness | 95% CI | Faithfulness | Answered | Context has answer | Correctness given context | Refused without context | Cited | Cited chunk has answer | Answer p50 / p95 (s) |\n");
            sb.Append("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|\n");
            foreach (var r in runs)
            {
                string M(string key) => r.Metrics.TryGetValue(key, out var v) && !double.IsNaN(v) ? v.ToString("F3", inv) : "–";
                sb.Append(inv, $"| {r.Run.Name} | **{M("correctness")}** | {r.CorrectnessCi95[0]:F3}–{r.CorrectnessCi95[1]:F3} | {M("faithfulness")} | {M("answered")} | ");
                sb.Append(inv, $"{M("context_has_answer")} | {M("correctness_given_context")} | {M("refused_without_context")} | {M("cited")} | {M("cited_has_answer")} | ");
                sb.Append(inv, $"{r.Answer.P50 / 1000:F1} / {r.Answer.P95 / 1000:F1} |\n");
            }
        }

        sb.Append("\nCorrectness: judged against the dataset's answer span, yes = 1, partial = 0.5, no = 0; refusals count 0. ")
          .Append("Faithfulness: every statement supported by the passages given to the model, over answered questions. ")
          .Append("Context has answer: a passage given to the model contains the answer span. ")
          .Append("Refused without context: share of refusals among questions whose passages lack the answer (refusing is right there). ")
          .Append("Cited: answers with at least one valid citation. The judge's agreement with human labels is in results/README.md.\n");
        return sb.ToString();
    }
}
