namespace RagEval.Core.Indexing;

/// <summary>Finds the chunks most relevant to a question. Dense, sparse and hybrid strategies implement it.</summary>
public interface IRetriever
{
    /// <summary>A short name for reports, e.g. <c>dense</c>.</summary>
    string Name { get; }

    /// <summary>Whether <see cref="RetrieveAsync"/> uses the query embedding (sparse retrieval does not).</summary>
    bool NeedsQueryVector => true;

    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Nearest neighbours of the query embedding.</summary>
public sealed class DenseRetriever(ChunkStore store) : IRetriever
{
    public string Name => "dense";

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
        CancellationToken cancellationToken = default) => store.SearchAsync(queryVector, limit, cancellationToken);
}

/// <summary>BM25 over the chunks' terms, from a collection built with <see cref="ChunkStore.EnsureSparseCollectionAsync"/>.</summary>
public sealed class SparseRetriever(ChunkStore store, Bm25Encoder encoder) : IRetriever
{
    public string Name => $"bm25-{encoder.Analyzer.Name}";

    public bool NeedsQueryVector => false;

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
        CancellationToken cancellationToken = default) => store.SearchSparseAsync(encoder.EncodeQuery(query), limit, cancellationToken);
}

/// <summary>
/// Reciprocal rank fusion (Cormack et al., 2009) of two retrievers: each chunk scores
/// Σ 1/(k + rank) over the lists it appears in. Rank-based, so the dense and BM25 scores, which
/// live on different scales, need no calibration.
/// </summary>
public sealed class HybridRetriever(IRetriever first, IRetriever second, int k = 60, int candidates = 100) : IRetriever
{
    public string Name => $"rrf({first.Name}, {second.Name})";

    public bool NeedsQueryVector => first.NeedsQueryVector || second.NeedsQueryVector;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
        CancellationToken cancellationToken = default)
    {
        var depth = Math.Max(limit, candidates);
        var lists = await Task.WhenAll(
            first.RetrieveAsync(query, queryVector, depth, cancellationToken),
            second.RetrieveAsync(query, queryVector, depth, cancellationToken));
        return Fuse(lists, k, limit);
    }

    public static IReadOnlyList<RetrievedChunk> Fuse(IEnumerable<IReadOnlyList<RetrievedChunk>> lists, int k, int limit)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var fused = new Dictionary<string, (RetrievedChunk Chunk, double Score, int BestRank)>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            for (var rank = 1; rank <= list.Count; rank++)
            {
                var hit = list[rank - 1];
                var contribution = 1.0 / (k + rank);
                fused[hit.Chunk.Id] = fused.TryGetValue(hit.Chunk.Id, out var seen)
                    ? (seen.Chunk, seen.Score + contribution, Math.Min(seen.BestRank, rank))
                    : (hit, contribution, rank);
            }
        }

        return [.. fused.Values
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.BestRank)
            .ThenBy(f => f.Chunk.Chunk.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(f => f.Chunk with { Score = f.Score })];
    }
}
