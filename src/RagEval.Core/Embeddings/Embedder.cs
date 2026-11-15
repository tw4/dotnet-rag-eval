using Microsoft.Extensions.AI;

namespace RagEval.Core.Embeddings;

/// <summary>Embeds documents and queries with the prefixes their model expects, in batches.</summary>
public sealed class Embedder(IEmbeddingGenerator<string, Embedding<float>> generator, EmbeddingProfile profile, int batchSize = 32)
{
    public EmbeddingProfile Profile { get; } = profile;

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var batch in texts.Chunk(batchSize))
        {
            var embeddings = await generator.GenerateAsync(batch.Select(t => Profile.DocumentPrefix + t), cancellationToken: cancellationToken);
            vectors.AddRange(embeddings.Select(e => e.Vector));
        }

        return vectors;
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var embeddings = await generator.GenerateAsync([Profile.QueryPrefix + query], cancellationToken: cancellationToken);
        return embeddings[0].Vector;
    }
}
