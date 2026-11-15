namespace RagEval.Core.Indexing;

/// <summary>Finds the chunks most relevant to a question. Dense, sparse and hybrid strategies implement it.</summary>
public interface IRetriever
{
    /// <summary>A short name for reports, e.g. <c>dense</c>.</summary>
    string Name { get; }

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
