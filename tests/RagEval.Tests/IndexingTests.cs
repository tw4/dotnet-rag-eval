using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Ingestion.Loaders;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public class IndexingTests
{
    private static Document Md(string id, string markdown) => new MarkdownLoader().Load(id, id, markdown);

    [Theory]
    [InlineData("nomic-embed-text", "search_document: ", "search_query: ")]
    [InlineData("nomic-embed-text:v1.5", "search_document: ", "search_query: ")]
    [InlineData("mxbai-embed-large:latest", "", "Represent this sentence for searching relevant passages: ")]
    [InlineData("jeffh/intfloat-multilingual-e5-large:f16", "passage: ", "query: ")]
    [InlineData("bge-m3", "", "")]
    [InlineData("all-minilm", "", "")]
    public void Embedding_profiles_add_the_prefixes_a_model_expects(string model, string document, string query)
    {
        var profile = EmbeddingProfile.For(model);
        Assert.Equal((document, query), (profile.DocumentPrefix, profile.QueryPrefix));
    }

    [Fact]
    public void Point_ids_are_stable_uuid_v5()
    {
        var a = DeterministicGuid.Create("guide.md#3");
        Assert.Equal(a, DeterministicGuid.Create("guide.md#3"));
        Assert.NotEqual(a, DeterministicGuid.Create("guide.md#4"));
        var s = a.ToString();
        Assert.Equal('5', s[14]);                   // version
        Assert.Contains(s[19], "89ab");             // RFC variant
    }

    [Fact]
    public async Task Embedder_prefixes_and_batches()
    {
        var generator = new HashingEmbeddingGenerator();
        var embedder = new Embedder(generator, EmbeddingProfile.For("nomic-embed-text"), batchSize: 2);

        var vectors = await embedder.EmbedDocumentsAsync(["a", "b", "c"], TestContext.Current.CancellationToken);
        await embedder.EmbedQueryAsync("q", TestContext.Current.CancellationToken);

        Assert.Equal(3, vectors.Count);
        Assert.Equal([2, 1, 1], generator.BatchSizes);
        Assert.Equal(["search_document: a", "search_document: b", "search_document: c", "search_query: q"], generator.Inputs);
    }

    [Fact]
    public void Payload_round_trips_everything_a_citation_needs()
    {
        var doc = Md("guide.md", "# Guide\n\nSome text about installing.\n");
        var chunk = new FixedSizeChunker(100, 10).Split(doc)[0];

        var point = ChunkStore.ToPoint(doc, chunk, new float[] { 1, 0 });
        var back = ChunkStore.FromPayload(point.Payload, 0.5);

        Assert.Equal(chunk, back.Chunk);
        Assert.Equal("Guide", back.Title);
        Assert.Equal("guide.md", back.Source);
        Assert.Equal(0.5, back.Score);
    }

    [Fact]
    public async Task Indexes_searches_and_replaces_documents_in_qdrant()
    {
        using var client = QdrantFixture.ClientOrSkip();
        var collection = QdrantFixture.NewCollection();
        try
        {
            var generator = new HashingEmbeddingGenerator();
            var embedder = new Embedder(generator, EmbeddingProfile.For("test"));
            var store = new ChunkStore(client, collection);
            var indexer = new Indexer(new FixedSizeChunker(60, 10), embedder, store);
            var ct = TestContext.Current.CancellationToken;

            await indexer.IndexAsync(Md("pets.md", "# Pets\n\nCats sleep most of the day. Dogs enjoy long walks in the park.\n"), ct);
            await indexer.IndexAsync(Md("space.md", "# Space\n\nThe moon orbits the earth. Mars has two small moons.\n"), ct);
            var before = await store.CountAsync(ct);

            var hits = await store.SearchAsync(await embedder.EmbedQueryAsync("how many moons does mars have", ct), 2, ct);
            Assert.Equal("space.md", hits[0].Chunk.DocumentId);
            Assert.Contains("Mars", hits[0].Chunk.Text, StringComparison.Ordinal);
            Assert.True(hits[0].Score >= hits[1].Score);

            // A shorter new version of a document leaves no stale chunks behind.
            await indexer.IndexAsync(Md("pets.md", "# Pets\n\nCats.\n"), ct);
            var after = await store.CountAsync(ct);
            Assert.True(after < before, $"expected fewer points after shrinking pets.md ({before} -> {after})");

            // A collection with another vector size is refused with a clear message.
            var other = new Indexer(new FixedSizeChunker(60, 10), new Embedder(new HashingEmbeddingGenerator(32), EmbeddingProfile.For("x")), store);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => other.IndexAsync(Md("x.md", "hello world"), ct));
            Assert.Contains("64-dimensional", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await client.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
        }
    }
}
