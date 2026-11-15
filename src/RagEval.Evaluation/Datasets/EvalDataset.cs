using System.Text.Json;
using RagEval.Core.Documents;
using RagEval.Ingestion.Loaders;

namespace RagEval.Evaluation.Datasets;

/// <summary>A corpus document as the dataset gives it.</summary>
public sealed record CorpusDocument(string Id, string Title, string Text);

/// <summary>A test query; <paramref name="AnswerSpan"/> is the verbatim answer where the dataset has one.</summary>
public sealed record EvalQuery(string Id, string Text, string? AnswerSpan = null);

/// <summary>A loaded dataset: corpus, the test queries that have judgements, and the judgements.</summary>
public sealed class EvalDataset
{
    public required DatasetInfo Info { get; init; }

    public required IReadOnlyList<CorpusDocument> Corpus { get; init; }

    public required IReadOnlyList<EvalQuery> Queries { get; init; }

    /// <summary>query id → (document id → graded relevance, &gt; 0 means relevant).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Qrels { get; init; }

    /// <summary>The corpus as pipeline documents: title, a blank line, the text (section markers become headings).</summary>
    public IEnumerable<Document> Documents() =>
        Corpus.Select(d => PlainTextLoader.Load(d.Id, $"{Info.Id}:{d.Id}",
            string.IsNullOrWhiteSpace(d.Title) ? d.Text : d.Title + "\n\n" + d.Text, d.Title));

    /// <summary>Loads a downloaded dataset from <paramref name="dataDirectory"/>.</summary>
    public static EvalDataset Load(DatasetInfo info, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(info);
        var folder = Path.Combine(dataDirectory, info.Id);
        var corpus = ReadJsonLines(Path.Combine(folder, "corpus.jsonl"))
            .Select(o => new CorpusDocument(Str(o, "_id"), Str(o, "title"), Str(o, "text")))
            .ToList();

        var qrelsPath = Path.Combine(folder, "qrels", info.Split + (info.Format == "beir-zip" ? ".tsv" : ".jsonl"));
        var qrels = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (query, doc, score) in info.Format == "beir-zip" ? ReadQrelsTsv(qrelsPath) : ReadQrelsJsonl(qrelsPath))
        {
            if (!qrels.TryGetValue(query, out var docs))
            {
                qrels[query] = docs = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            docs[doc] = score;
        }

        var queries = ReadJsonLines(Path.Combine(folder, "queries.jsonl"))
            .Select(o => new EvalQuery(Str(o, "_id"), Str(o, "text"),
                o.TryGetProperty("answer_span", out var span) && span.ValueKind == JsonValueKind.String ? span.GetString() : null))
            .Where(q => qrels.ContainsKey(q.Id))
            .ToList();

        return new EvalDataset
        {
            Info = info,
            Corpus = corpus,
            Queries = queries,
            Qrels = qrels.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value, StringComparer.Ordinal),
        };
    }

    private static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";

    private static IEnumerable<JsonElement> ReadJsonLines(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length > 0)
            {
                using var doc = JsonDocument.Parse(line);
                yield return doc.RootElement.Clone();
            }
        }
    }

    private static IEnumerable<(string Query, string Doc, int Score)> ReadQrelsTsv(string path) =>
        File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
            .Select(p => (p[0], p[1], int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)));

    private static IEnumerable<(string Query, string Doc, int Score)> ReadQrelsJsonl(string path) =>
        ReadJsonLines(path).Select(o => (Str(o, "query-id"), Str(o, "corpus-id"), o.GetProperty("score").GetInt32()));
}
