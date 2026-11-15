using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;

namespace RagEval.Evaluation.Datasets;

/// <summary>What <see cref="CorpusIndex.EnsureAsync"/> did.</summary>
public sealed record IndexResult(string Collection, int Documents, int Chunks, long Characters, TimeSpan Elapsed, bool Reused,
    int Dimensions = 0, long? SparseBytes = null)
{
    /// <summary>
    /// Raw size of the stored vectors, in bytes: float32 per dimension for dense vectors, or for
    /// sparse ones 8 bytes (a 32-bit id and a float32 weight) per non-zero entry.
    /// </summary>
    public long VectorBytes => SparseBytes ?? (long)Chunks * Dimensions * sizeof(float);
}

/// <summary>
/// Indexes a dataset's corpus into its own Qdrant collection, one per configuration (dataset,
/// chunker, embedding model). A complete index is reused: the configuration's fingerprint is
/// stored in the collection metadata once indexing finishes, so an interrupted run is redone.
/// </summary>
public sealed class CorpusIndex(QdrantClient client, IChunker chunker, Embedder embedder, int batchSize = 64)
{
    /// <summary>Bump when loading or chunking changes in a way that invalidates existing indexes.</summary>
    public const string FormatVersion = "1";

    public static string Fingerprint(EvalDataset dataset, IChunker chunker, EmbeddingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(profile);
        var files = string.Join(",", dataset.Info.Files.Select(f => f.Sha256));
        return string.Join("|", FormatVersion, dataset.Info.Id, files, chunker.Name, profile.Model, profile.DocumentPrefix);
    }

    public static string CollectionName(EvalDataset dataset, IChunker chunker, EmbeddingProfile profile)
    {
        var fingerprint = Fingerprint(dataset, chunker, profile);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..8];
        var model = new string([.. profile.Model.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        return $"eval-{dataset.Info.Id}-{chunker.Name}-{model}-{hash}";
    }

    public async Task<IndexResult> EnsureAsync(EvalDataset dataset, bool force = false, Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var name = CollectionName(dataset, chunker, embedder.Profile);
        var fingerprint = Fingerprint(dataset, chunker, embedder.Profile);
        var store = new ChunkStore(client, name);

        var metadata = await store.GetMetadataAsync(cancellationToken);
        if (!force && metadata is not null && metadata.GetValueOrDefault("fingerprint") == fingerprint &&
            metadata.GetValueOrDefault("complete") == "true")
        {
            return new IndexResult(name, int.Parse(metadata["documents"], CultureInfo.InvariantCulture),
                int.Parse(metadata["chunks"], CultureInfo.InvariantCulture), long.Parse(metadata["characters"], CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(double.Parse(metadata.GetValueOrDefault("seconds", "0"), CultureInfo.InvariantCulture)), Reused: true,
                metadata.TryGetValue("dimensions", out var d) ? int.Parse(d, CultureInfo.InvariantCulture)
                    : await store.GetDimensionsAsync(cancellationToken));
        }

        var watch = Stopwatch.StartNew();
        var pending = new List<(Document Document, Chunk Chunk)>();
        int documents = 0, chunks = 0;
        long characters = 0;
        var created = false;
        var dimensions = 0;

        async Task FlushAsync()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var vectors = await embedder.EmbedDocumentsAsync([.. pending.Select(p => p.Chunk.TextToEmbed)], cancellationToken);
            if (!created)
            {
                dimensions = vectors[0].Length;
                await store.EnsureCollectionAsync(dimensions, recreate: true, cancellationToken);
                created = true;
            }

            await store.UpsertAsync([.. pending.Select((p, i) => (p.Document, p.Chunk, vectors[i]))], cancellationToken);
            chunks += pending.Count;
            pending.Clear();
        }

        foreach (var document in dataset.Documents())
        {
            foreach (var chunk in chunker.Split(document))
            {
                pending.Add((document, chunk));
                characters += chunk.Text.Length;
                if (pending.Count >= batchSize)
                {
                    await FlushAsync();
                }
            }

            documents++;
            if (documents % 500 == 0)
            {
                log?.Invoke($"  {documents}/{dataset.Corpus.Count} documents, {chunks} chunks, {watch.Elapsed.TotalSeconds:F0} s");
            }
        }

        await FlushAsync();
        await store.SetMetadataAsync(new Dictionary<string, string>
        {
            ["fingerprint"] = fingerprint,
            ["complete"] = "true",
            ["documents"] = documents.ToString(CultureInfo.InvariantCulture),
            ["chunks"] = chunks.ToString(CultureInfo.InvariantCulture),
            ["characters"] = characters.ToString(CultureInfo.InvariantCulture),
            ["seconds"] = watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
            ["dimensions"] = dimensions.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);
        return new IndexResult(name, documents, chunks, characters, watch.Elapsed, Reused: false, dimensions);
    }
}
