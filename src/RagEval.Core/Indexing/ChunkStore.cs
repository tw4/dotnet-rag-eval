using Qdrant.Client;
using Qdrant.Client.Grpc;
using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using static Qdrant.Client.Grpc.Conditions;
using Document = RagEval.Core.Documents.Document;

namespace RagEval.Core.Indexing;

/// <summary>A chunk returned by a search, with its score.</summary>
public sealed record RetrievedChunk(Chunk Chunk, double Score, string? Title, string? Source);

/// <summary>
/// Stores chunks and their vectors in a Qdrant collection (named vector <c>dense</c>, cosine), with
/// everything needed to cite a chunk in its payload.
/// </summary>
public sealed class ChunkStore(QdrantClient client, string collection)
{
    public const string DenseVector = "dense";

    public string Collection { get; } = collection;

    /// <summary>Creates the collection if needed; fails if it exists with another vector size.</summary>
    public async Task EnsureCollectionAsync(int dimensions, bool recreate = false, CancellationToken cancellationToken = default)
    {
        var exists = await client.CollectionExistsAsync(Collection, cancellationToken);
        if (exists && recreate)
        {
            await client.DeleteCollectionAsync(Collection, cancellationToken: cancellationToken);
            exists = false;
        }

        if (exists)
        {
            var info = await client.GetCollectionInfoAsync(Collection, cancellationToken);
            var size = info.Config.Params.VectorsConfig.ParamsMap?.Map.TryGetValue(DenseVector, out var p) == true ? (int)p.Size : -1;
            if (size != dimensions)
            {
                throw new InvalidOperationException(
                    $"Collection '{Collection}' holds {(size < 0 ? "vectors without a 'dense' vector" : $"{size}-dimensional vectors")}, " +
                    $"but the embedding model produces {dimensions}. Use another collection or recreate it.");
            }

            return;
        }

        await client.CreateCollectionAsync(Collection,
            new VectorParamsMap { Map = { [DenseVector] = new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine } } },
            cancellationToken: cancellationToken);
        await client.CreatePayloadIndexAsync(Collection, "document_id", PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
    }

    /// <summary>Replaces every chunk of a document: stale chunks from an earlier version are removed.</summary>
    public async Task ReplaceDocumentAsync(Document document, IReadOnlyList<Chunk> chunks, IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(vectors);
        if (chunks.Count != vectors.Count)
        {
            throw new ArgumentException("One vector per chunk is required.", nameof(vectors));
        }

        await client.DeleteAsync(Collection, MatchKeyword("document_id", document.Id), cancellationToken: cancellationToken);
        if (chunks.Count == 0)
        {
            return;
        }

        var points = new List<PointStruct>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            points.Add(ToPoint(document, chunks[i], vectors[i]));
        }

        foreach (var batch in points.Chunk(256))
        {
            await client.UpsertAsync(Collection, batch, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Adds chunks of many documents in bulk (no deletes: for freshly created collections).</summary>
    public async Task UpsertAsync(IReadOnlyList<(Document Document, Chunk Chunk, ReadOnlyMemory<float> Vector)> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var batch in items.Chunk(256))
        {
            await client.UpsertAsync(Collection, [.. batch.Select(i => ToPoint(i.Document, i.Chunk, i.Vector))],
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>The collection's metadata, or null if the collection does not exist.</summary>
    public async Task<IReadOnlyDictionary<string, string>?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (!await client.CollectionExistsAsync(Collection, cancellationToken))
        {
            return null;
        }

        var info = await client.GetCollectionInfoAsync(Collection, cancellationToken);
        return info.Config.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value.StringValue, StringComparer.Ordinal);
    }

    public async Task SetMetadataAsync(IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        await client.UpdateCollectionAsync(Collection, metadata.ToDictionary(kv => kv.Key, kv => (Value)kv.Value),
            cancellationToken: cancellationToken);
    }

    /// <summary>The <paramref name="limit"/> chunks closest to a query vector.</summary>
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken cancellationToken = default)
    {
        var points = await client.QueryAsync(Collection, query: vector.ToArray(), usingVector: DenseVector,
            limit: (ulong)limit, payloadSelector: true, cancellationToken: cancellationToken);
        return [.. points.Select(p => FromPayload(p.Payload, p.Score))];
    }

    public async Task<ulong> CountAsync(CancellationToken cancellationToken = default) =>
        await client.CollectionExistsAsync(Collection, cancellationToken)
            ? await client.CountAsync(Collection, exact: true, cancellationToken: cancellationToken)
            : 0;

    internal static PointStruct ToPoint(Document document, Chunk chunk, ReadOnlyMemory<float> vector)
    {
        var point = new PointStruct
        {
            Id = DeterministicGuid.Create(chunk.Id),
            Vectors = new Dictionary<string, float[]> { [DenseVector] = vector.ToArray() },
            Payload =
            {
                ["document_id"] = chunk.DocumentId,
                ["chunk_index"] = chunk.Index,
                ["text"] = chunk.Text,
                ["text_start"] = chunk.TextStart,
                ["text_end"] = chunk.TextEnd,
                ["source_start"] = chunk.Source.Start,
                ["source_end"] = chunk.Source.End,
                ["start_line"] = chunk.Source.StartLine,
                ["end_line"] = chunk.Source.EndLine,
                ["source"] = document.Source,
                ["format"] = document.Format.ToString().ToLowerInvariant(),
            },
        };
        if (chunk.Source.Page is { } page)
        {
            point.Payload["page"] = page;
        }

        if (chunk.Section is { } section)
        {
            point.Payload["section"] = section;
        }

        if (document.Title is { } title)
        {
            point.Payload["title"] = title;
        }

        return point;
    }

    internal static RetrievedChunk FromPayload(IDictionary<string, Value> payload, double score)
    {
        string S(string key) => payload.TryGetValue(key, out var v) ? v.StringValue : "";
        int I(string key) => payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;
        string? Opt(string key) => payload.TryGetValue(key, out var v) && v.HasStringValue ? v.StringValue : null;
        int? Page = payload.TryGetValue("page", out var pv) ? (int)pv.IntegerValue : null;

        var span = new SourceSpan(I("source_start"), I("source_end"), I("start_line"), I("end_line"), Page);
        var chunk = new Chunk(S("document_id"), I("chunk_index"), S("text"), I("text_start"), I("text_end"), span, Opt("section"));
        return new RetrievedChunk(chunk, score, Opt("title"), Opt("source"));
    }
}
