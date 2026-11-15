using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RagEval.Evaluation.Experiments;

/// <summary>Writes an experiment's raw results (results.json) and a Markdown summary (summary.md).</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>Writes both files into <paramref name="directory"/> and returns their paths.</summary>
    public static (string Json, string Markdown) Write(ExperimentResult result, string directory)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, "results.json");
        var markdown = Path.Combine(directory, "summary.md");
        File.WriteAllText(json, JsonSerializer.Serialize(result, Json));
        File.WriteAllText(markdown, Markdown(result));
        return (json, markdown);
    }

    public static ExperimentResult Read(string path) =>
        JsonSerializer.Deserialize<ExperimentResult>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Cannot read results {path}.");

    public static string Markdown(ExperimentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var inv = CultureInfo.InvariantCulture;
        var e = result.Environment;
        var sb = new StringBuilder();
        sb.Append(inv, $"# {result.Experiment.Name}\n\n");
        if (result.Experiment.Description.Length > 0)
        {
            sb.Append(result.Experiment.Description).Append("\n\n");
        }

        sb.Append(inv, $"Run {result.StartedAt:yyyy-MM-dd HH:mm} UTC, {(result.FinishedAt - result.StartedAt).TotalMinutes:F1} min, on {e.Cpu}");
        if (e.GitCommit is not null)
        {
            sb.Append(inv, $" · commit `{e.GitCommit[..7]}`{(e.GitDirty == true ? " (uncommitted changes)" : "")}");
        }

        sb.Append(inv, $" · Ollama {e.OllamaVersion ?? "?"} · Qdrant {e.QdrantVersion ?? "?"}\n");
        if (e.ModelDigests.Count > 0)
        {
            sb.Append("Models: ").Append(string.Join(", ", e.ModelDigests.Select(m => $"`{m.Key}` ({m.Value[..12]})"))).Append('\n');
        }

        if (result.QueryLimit is { } limit)
        {
            sb.Append(inv, $"\n> Quick run: only the first {limit} queries of each dataset. Not comparable with full runs.\n");
        }

        foreach (var group in result.Runs.GroupBy(r => r.Dataset))
        {
            var runs = group.ToList();
            var answers = runs.Any(r => r.Metrics.ContainsKey("answer_hit@5"));
            sb.Append(inv, $"\n## {group.Key} ({runs[0].Queries} queries)\n\n");
            sb.Append("| Run | nDCG@10 | 95% CI | Recall@10 | Recall@100 | MRR@10 |");
            sb.Append(answers ? " Answer hit@5 | Answer hit@10 |" : "");
            sb.Append(" Chunks | Text (MB) | Vectors (MB) | Index (s) | Query embed p50 (ms) | Search p50 (ms) |\n");
            sb.Append("|---|---:|---|---:|---:|---:|").Append(answers ? "---:|---:|" : "").Append("---:|---:|---:|---:|---:|---:|\n");
            foreach (var r in runs)
            {
                string M(string key) => r.Metrics.TryGetValue(key, out var v) ? v.ToString("F3", inv) : "–";
                sb.Append(inv, $"| {r.Run.Name} | **{M("ndcg@10")}** | {r.Ndcg10Ci95[0]:F3}–{r.Ndcg10Ci95[1]:F3} | {M("recall@10")} | {M("recall@100")} | {M("mrr@10")} |");
                if (answers)
                {
                    sb.Append(inv, $" {M("answer_hit@5")} | {M("answer_hit@10")} |");
                }

                var vectors = r.Index.VectorBytes > 0 ? (r.Index.VectorBytes / 1e6).ToString("F1", inv) : "–";
                var seconds = r.Index.Seconds > 0 ? r.Index.Seconds.ToString("F0", inv) : "–";
                var embed = r.Run.Retrieval.Strategy == "sparse" ? "–" : r.QueryEmbedding.P50.ToString("F0", inv);
                sb.Append(inv, $" {r.Index.Chunks:N0} | {r.Index.Characters / 1e6:F2} | {vectors} | {seconds} | {embed} | {r.Search.P50:F1} |\n");
            }
        }

        sb.Append("\nnDCG@10, recall and MRR are document-level (each document ranked at its best chunk), computed as trec_eval does. ")
          .Append("Answer hit@k: a top-k chunk from a relevant document contains the answer span. ")
          .Append("Text: characters stored in chunk payloads; Vectors: raw vector size (dense: 4 bytes per dimension; sparse: 8 bytes per non-zero term); Index: time to build the index. ")
          .Append("The 95% CI is a bootstrap over queries: runs whose intervals overlap widely are not reliably different.\n");
        return sb.ToString();
    }
}
