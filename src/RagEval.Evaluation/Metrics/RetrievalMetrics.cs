using RagEval.Core.Indexing;

namespace RagEval.Evaluation.Metrics;

/// <summary>
/// Ranking metrics as trec_eval (and so BEIR) computes them: on a ranked list of document ids against
/// graded judgements, where a grade above zero means relevant and gains are linear in the grade.
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>Relevant documents in the top <paramref name="k"/>, divided by all relevant documents.</summary>
    public static double RecallAt(IReadOnlyList<string> ranking, IReadOnlyDictionary<string, int> judgements, int k)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentNullException.ThrowIfNull(judgements);
        var relevant = judgements.Count(j => j.Value > 0);
        if (relevant == 0)
        {
            return 0;
        }

        var found = ranking.Take(k).Count(id => judgements.TryGetValue(id, out var grade) && grade > 0);
        return (double)found / relevant;
    }

    /// <summary>1 / rank of the first relevant document within the top <paramref name="k"/>, else 0.</summary>
    public static double ReciprocalRankAt(IReadOnlyList<string> ranking, IReadOnlyDictionary<string, int> judgements, int k)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentNullException.ThrowIfNull(judgements);
        for (var i = 0; i < Math.Min(k, ranking.Count); i++)
        {
            if (judgements.TryGetValue(ranking[i], out var grade) && grade > 0)
            {
                return 1.0 / (i + 1);
            }
        }

        return 0;
    }

    /// <summary>Normalized discounted cumulative gain at <paramref name="k"/>, gain = grade, discount = log2(rank + 1).</summary>
    public static double NdcgAt(IReadOnlyList<string> ranking, IReadOnlyDictionary<string, int> judgements, int k)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentNullException.ThrowIfNull(judgements);
        var dcg = 0.0;
        for (var i = 0; i < Math.Min(k, ranking.Count); i++)
        {
            if (judgements.TryGetValue(ranking[i], out var grade) && grade > 0)
            {
                dcg += grade / Math.Log2(i + 2);
            }
        }

        var ideal = judgements.Values.Where(g => g > 0).OrderDescending().Take(k).Select((g, i) => g / Math.Log2(i + 2)).Sum();
        return ideal == 0 ? 0 : dcg / ideal;
    }

    /// <summary>
    /// Document ranking from a chunk ranking: each document takes the rank of its best chunk
    /// (chunks are given best first), duplicates dropped.
    /// </summary>
    public static IReadOnlyList<string> DocumentRanking(IEnumerable<RetrievedChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. chunks.OrderByDescending(c => c.Score).Select(c => c.Chunk.DocumentId).Where(seen.Add)];
    }

    /// <summary>
    /// Chunk-level hit: does a chunk in the top <paramref name="k"/> come from a relevant document and
    /// contain the answer span? Matching ignores case and whitespace differences.
    /// </summary>
    public static bool AnswerHitAt(IReadOnlyList<RetrievedChunk> chunks, IReadOnlyDictionary<string, int> judgements, string answerSpan, int k)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(judgements);
        var needle = Normalize(answerSpan);
        return needle.Length > 0 && chunks.Take(k).Any(c =>
            judgements.TryGetValue(c.Chunk.DocumentId, out var grade) && grade > 0 &&
            Normalize(c.Chunk.Text).Contains(needle, StringComparison.Ordinal));
    }

    internal static string Normalize(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
