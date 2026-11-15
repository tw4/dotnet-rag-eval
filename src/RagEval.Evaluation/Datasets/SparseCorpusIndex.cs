using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using RagEval.Core.Chunking;
using RagEval.Core.Indexing;
using Document = RagEval.Core.Documents.Document;

namespace RagEval.Evaluation.Datasets;

/// <summary>
/// The BM25 counterpart of <see cref="CorpusIndex"/>: the same chunks (same ids, so results can be
/// fused with a dense index of the same chunker) with sparse vectors, in their own collection.
/// No model is involved, so indexing takes seconds. Reused while the configuration is unchanged.
/// </summary>
public sealed class SparseCorpusIndex(QdrantClient client, IChunker chunker, TextAnalyzer analyzer, double k1 = 1.2, double b = 0.75)
{
    public const string FormatVersion = "1";

    public string Fingerprint(EvalDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var files = string.Join(",", dataset.Info.Files.Select(f => f.Sha256));
        return string.Join("|", CorpusIndex.FormatVersion, FormatVersion, dataset.Info.Id, files, chunker.Name, "bm25", analyzer.Name,
            k1.ToString("R", CultureInfo.InvariantCulture), b.ToString("R", CultureInfo.InvariantCulture));
    }

    public string CollectionName(EvalDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Fingerprint(dataset))))[..8];
        return $"eval-{dataset.Info.Id}-{chunker.Name}-bm25-{analyzer.Name}-{hash}";
    }

    /// <summary>Builds the index if needed, and returns it with the encoder that queries it.</summary>
    public async Task<(IndexResult Index, Bm25Encoder Encoder)> EnsureAsync(EvalDataset dataset, bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var name = CollectionName(dataset);
        var fingerprint = Fingerprint(dataset);
        var store = new ChunkStore(client, name);
        static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        var metadata = await store.GetMetadataAsync(cancellationToken);
        if (!force && metadata is not null && metadata.GetValueOrDefault("fingerprint") == fingerprint &&
            metadata.GetValueOrDefault("complete") == "true")
        {
            var reused = new IndexResult(name, int.Parse(metadata["documents"], CultureInfo.InvariantCulture),
                int.Parse(metadata["chunks"], CultureInfo.InvariantCulture), long.Parse(metadata["characters"], CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(double.Parse(metadata["seconds"], CultureInfo.InvariantCulture)), Reused: true,
                SparseBytes: long.Parse(metadata["sparse_bytes"], CultureInfo.InvariantCulture));
            var average = double.Parse(metadata["average_terms"], CultureInfo.InvariantCulture);
            return (reused, new Bm25Encoder(analyzer, average, k1, b));
        }

        var watch = Stopwatch.StartNew();
        var items = new List<(Document Document, Chunk Chunk)>();
        var documents = 0;
        foreach (var document in dataset.Documents())
        {
            items.AddRange(chunker.Split(document).Select(c => (document, c)));
            documents++;
        }

        var encoder = new Bm25Encoder(analyzer, Bm25Encoder.AverageTerms(items.Select(i => i.Chunk.TextToEmbed), analyzer), k1, b);
        var encoded = items.Select(i => (i.Document, i.Chunk, encoder.EncodeDocument(i.Chunk.TextToEmbed))).ToList();
        await store.EnsureSparseCollectionAsync(recreate: true, cancellationToken);
        await store.UpsertSparseAsync(encoded, cancellationToken);

        var characters = items.Sum(i => (long)i.Chunk.Text.Length);
        var sparseBytes = encoded.Sum(e => (long)e.Item3.Count * 8);
        await store.SetMetadataAsync(new Dictionary<string, string>
        {
            ["fingerprint"] = fingerprint,
            ["complete"] = "true",
            ["documents"] = documents.ToString(CultureInfo.InvariantCulture),
            ["chunks"] = items.Count.ToString(CultureInfo.InvariantCulture),
            ["characters"] = characters.ToString(CultureInfo.InvariantCulture),
            ["seconds"] = watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
            ["sparse_bytes"] = sparseBytes.ToString(CultureInfo.InvariantCulture),
            ["average_terms"] = Inv(encoder.AverageLength),
        }, cancellationToken);
        return (new IndexResult(name, documents, items.Count, characters, watch.Elapsed, Reused: false, SparseBytes: sparseBytes), encoder);
    }
}
