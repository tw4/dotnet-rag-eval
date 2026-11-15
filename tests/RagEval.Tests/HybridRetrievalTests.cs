using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Indexing;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class HybridRetrievalTests
{
    [Fact]
    public void Analyzer_folds_case_and_turkish_i_and_splits_on_punctuation()
    {
        Assert.Equal(["istanbul", "da", "işik", "işik", "istanbul"],
            TextAnalyzer.Plain.Terms("İstanbul'da IŞIK ışık, İstanbul."));
        Assert.Equal(["dr", "smith", "s", "2024", "cats"], TextAnalyzer.Plain.Terms("Dr. Smith's 2024 CATS!"));
        Assert.Empty(TextAnalyzer.Plain.Terms(" -- ... "));
    }

    [Fact]
    public void Prefix_analyzer_cuts_words_but_keeps_numbers()
    {
        var prefix = TextAnalyzer.Parse("prefix-5");
        Assert.Equal("prefix-5", prefix.Name);
        Assert.Equal(["kitap", "kitap", "ev", "1234567", "a1b2c3d4"], prefix.Terms("kitaplarımızdan Kitapçı ev 1234567 a1b2c3d4"));
        Assert.Same(TextAnalyzer.Plain, TextAnalyzer.Parse("plain"));
        Assert.Throws<NotSupportedException>(() => TextAnalyzer.Parse("stem"));
        Assert.Throws<NotSupportedException>(() => TextAnalyzer.Parse("prefix-x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextAnalyzer.Parse("prefix-1"));
    }

    [Fact]
    public void Bm25_saturates_term_frequency_and_normalises_length()
    {
        var encoder = new Bm25Encoder(TextAnalyzer.Plain, averageLength: 4);
        var doc = encoder.EncodeDocument("cat cat cat dog");          // length 4 = average: no length penalty
        Assert.Equal(2, doc.Count);
        Assert.True(doc.Indices.SequenceEqual(doc.Indices.Order()));
        float Weight(SparseVector v, string term) => v.Values[Array.IndexOf(v.Indices, Bm25Encoder.TermId(term))];
        Assert.Equal(3 * 2.2 / (3 + 1.2), Weight(doc, "cat"), 5);
        Assert.Equal(1.0, Weight(doc, "dog"), 5);

        var longer = encoder.EncodeDocument("dog and seven more words in it");   // longer: same tf weighs less
        Assert.True(Weight(longer, "dog") < Weight(doc, "dog"));

        var query = encoder.EncodeQuery("Cat cat mouse");
        Assert.Equal(2f, Weight(query, "cat"));
        Assert.Equal(1f, Weight(query, "mouse"));
        Assert.Equal(4, Bm25Encoder.AverageTerms(["a b", "c d e f g h"], TextAnalyzer.Plain));
        Assert.NotEqual(Bm25Encoder.TermId("ab"), Bm25Encoder.TermId("ba"));
    }

    private static RetrievedChunk Hit(string doc, double score = 0) =>
        new(new Chunk(doc, 0, doc, 0, doc.Length, new SourceSpan(0, doc.Length, 1, 1, null)), score, null, null);

    [Fact]
    public void Reciprocal_rank_fusion_rewards_chunks_found_by_both()
    {
        var fused = HybridRetriever.Fuse([[Hit("a", 0.9), Hit("b", 0.8), Hit("c", 0.7)], [Hit("c", 12), Hit("d", 9)]], k: 60, limit: 10);

        Assert.Equal(["c", "a", "b", "d"], fused.Select(f => f.Chunk.DocumentId));   // b and d tie at rank 2: by id
        Assert.Equal((1.0 / 63) + (1.0 / 61), fused[0].Score, 10);
        Assert.Equal(1.0 / 61, fused[1].Score, 10);
        Assert.Equal(2, HybridRetriever.Fuse([[Hit("a"), Hit("b"), Hit("c")]], 60, 2).Count);
    }

    [Fact]
    public async Task Hybrid_retriever_asks_both_for_candidates_and_fuses()
    {
        var dense = new FixedRetriever("dense", [Hit("a"), Hit("b")], needsVector: true);
        var sparse = new FixedRetriever("bm25", [Hit("b"), Hit("c")], needsVector: false);
        var hybrid = new HybridRetriever(dense, sparse, k: 60, candidates: 50);

        var hits = await hybrid.RetrieveAsync("q", ReadOnlyMemory<float>.Empty, 5, TestContext.Current.CancellationToken);

        Assert.Equal(["b", "a", "c"], hits.Select(h => h.Chunk.DocumentId));
        Assert.Equal((50, 50), (dense.AskedFor, sparse.AskedFor));
        Assert.True(((IRetriever)hybrid).NeedsQueryVector);
        Assert.False(((IRetriever)sparse).NeedsQueryVector);
        Assert.Equal("rrf(dense, bm25)", hybrid.Name);
    }

    [Fact]
    public async Task Bm25_search_in_qdrant_applies_idf()
    {
        using var client = QdrantFixture.ClientOrSkip();
        var store = new ChunkStore(client, QdrantFixture.NewCollection());
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string[] texts =
            [
                "the cat sat on the mat",
                "the dog sat on the log",
                "the bird sang in the tree",
                "a zebra is the striped one",
            ];
            var encoder = new Bm25Encoder(TextAnalyzer.Plain, Bm25Encoder.AverageTerms(texts, TextAnalyzer.Plain));
            var items = texts.Select((t, i) =>
            {
                var builder = new TextBuilder();
                builder.Append(t, 0);
                var document = builder.Build($"d{i}", "test", DocumentFormat.Text, null, t);
                return (document, new Chunk(document.Id, 0, t, 0, t.Length, new SourceSpan(0, t.Length, 1, 1, null)), encoder.EncodeDocument(t));
            }).ToList();
            await store.EnsureSparseCollectionAsync(recreate: true, ct);
            await store.UpsertSparseAsync(items, ct);

            // "the" is everywhere and "sat" in two documents; "zebra" in one: rarity decides.
            var hits = await store.SearchSparseAsync(encoder.EncodeQuery("the zebra sat"), 10, ct);
            Assert.Equal("d3", hits[0].Chunk.DocumentId);
            Assert.Equal(["d0", "d1"], hits.Skip(1).Take(2).Select(h => h.Chunk.DocumentId).Order());
            Assert.Equal("the cat sat on the mat", hits.Single(h => h.Chunk.DocumentId == "d0").Chunk.Text);

            Assert.Empty(await store.SearchSparseAsync(encoder.EncodeQuery("?!"), 10, ct));
            Assert.Empty(await store.SearchSparseAsync(encoder.EncodeQuery("unicorn"), 10, ct));
        }
        finally
        {
            await client.DeleteCollectionAsync(store.Collection, cancellationToken: CancellationToken.None);
        }
    }

    private sealed class FixedRetriever(string name, IReadOnlyList<RetrievedChunk> hits, bool needsVector) : IRetriever
    {
        public int AskedFor { get; private set; }

        public string Name => name;

        public bool NeedsQueryVector => needsVector;

        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
            CancellationToken cancellationToken = default)
        {
            AskedFor = limit;
            return Task.FromResult(hits);
        }
    }
}
