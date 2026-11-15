using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Embeddings;

namespace RagEval.Core.Indexing;

/// <summary>Chunks a document, embeds the chunks and stores them, replacing any earlier version.</summary>
public sealed class Indexer(IChunker chunker, Embedder embedder, ChunkStore store)
{
    private bool _collectionReady;

    /// <summary>Drop and recreate the collection on first use.</summary>
    public bool Recreate { get; init; }

    /// <summary>Indexes one document and returns its chunks.</summary>
    public async Task<IReadOnlyList<Chunk>> IndexAsync(Document document, CancellationToken cancellationToken = default)
    {
        var chunks = chunker.Split(document);
        var vectors = await embedder.EmbedDocumentsAsync([.. chunks.Select(c => c.TextToEmbed)], cancellationToken);
        if (!_collectionReady)
        {
            var dimensions = vectors.Count > 0
                ? vectors[0].Length
                : (await embedder.EmbedQueryAsync("dimension probe", cancellationToken)).Length;
            await store.EnsureCollectionAsync(dimensions, Recreate, cancellationToken);
            _collectionReady = true;
        }

        await store.ReplaceDocumentAsync(document, chunks, vectors, cancellationToken);
        return chunks;
    }
}
