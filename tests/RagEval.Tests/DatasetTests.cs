using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using RagEval.Core.Chunking;
using RagEval.Core.Embeddings;
using RagEval.Evaluation.Datasets;
using RagEval.Ingestion.Loaders;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class DatasetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rag-eval-ds-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static DatasetInfo Info(string id, string format, params DatasetFile[] files) =>
        new(id, id, "en", "t", "CC BY 4.0", "x", "c", format, "test", files, new DatasetSize(2, 1, 1));

    private void WriteBeirJsonl(string id)
    {
        Directory.CreateDirectory(Path.Combine(_dir, id, "qrels"));
        File.WriteAllText(Path.Combine(_dir, id, "corpus.jsonl"),
            """
            {"_id": "d1", "title": "Diabetes", "text": "Intro line.\n\n== Symptoms ==\nThirst and tiredness.\n\n=== In children ===\nWeight loss."}
            {"_id": "d2", "title": "Flu", "text": "A viral infection."}
            """);
        File.WriteAllText(Path.Combine(_dir, id, "queries.jsonl"),
            """
            {"_id": "q1", "text": "Belirtiler neler?", "answer_span": "Thirst and tiredness"}
            {"_id": "q-unjudged", "text": "not in qrels"}
            """);
        File.WriteAllText(Path.Combine(_dir, id, "qrels", "test.jsonl"), """{"query-id": "q1", "corpus-id": "d1", "score": 1}""");
    }

    [Fact]
    public void Wiki_section_markers_become_headings_and_positions_stay_exact()
    {
        const string text = "Intro line.\n\n== Symptoms ==\nThirst and tiredness.\n\n=== In children ===\nWeight loss.\n= not a heading =";
        var doc = PlainTextLoader.Load("d1", "d1", text, "Diabetes");

        Assert.Equal("Intro line.\n\nSymptoms\n\nThirst and tiredness.\n\nIn children\n\nWeight loss. = not a heading =", doc.Text);
        Assert.Equal([(1, "Symptoms"), (2, "In children")], doc.Headings.Select(h => (h.Level, h.Title)));
        Assert.Equal("Diabetes", doc.Title);
        for (var i = 0; i < doc.Text.Length; i++)
        {
            if (!char.IsWhiteSpace(doc.Text[i]))
            {
                Assert.Equal(doc.Text[i], text[doc.Map.SourceOffset(i)]);
            }
        }

        var weight = doc.Text.IndexOf("Weight", StringComparison.Ordinal);
        Assert.Equal("Symptoms › In children", ChunkFactory.SectionAt(doc, weight));
    }

    [Fact]
    public void Loads_jsonl_datasets_with_answer_spans_and_only_judged_queries()
    {
        WriteBeirJsonl("tiny");
        var dataset = EvalDataset.Load(Info("tiny", "beir-jsonl"), _dir);

        Assert.Equal(["d1", "d2"], dataset.Corpus.Select(d => d.Id));
        var query = Assert.Single(dataset.Queries);
        Assert.Equal(("q1", "Thirst and tiredness"), (query.Id, query.AnswerSpan));
        Assert.Equal(1, dataset.Qrels["q1"]["d1"]);
        var doc = dataset.Documents().First();
        Assert.StartsWith("Diabetes\n\nIntro line.", doc.Text, StringComparison.Ordinal);
        Assert.Equal("tiny:d1", doc.Source);
    }

    [Fact]
    public async Task Downloads_verifies_checksums_and_unpacks_beir_archives()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(archive.CreateEntry(name).Open());
                w.Write(content);
            }

            Add("beirz/corpus.jsonl", """{"_id": "7", "title": "T", "text": "abstract"}""");
            Add("beirz/queries.jsonl", """{"_id": "1", "text": "claim"}""");
            Add("beirz/qrels/test.tsv", "query-id\tcorpus-id\tscore\n1\t7\t1\n");
        }

        var bytes = zip.ToArray();
        using var handler = new StaticHandler(bytes);
        using var http = new HttpClient(handler);
        var downloader = new DatasetDownloader(http, _dir);

        var info = Info("beirz", "beir-zip", new DatasetFile("https://example.test/beirz.zip", "beirz.zip", Sha(bytes)));
        Assert.Equal(1, await downloader.EnsureAsync(info, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, await downloader.EnsureAsync(info, cancellationToken: TestContext.Current.CancellationToken));   // cached
        var dataset = EvalDataset.Load(info, _dir);
        Assert.Equal(("7", "1"), (dataset.Corpus.Single().Id, dataset.Queries.Single().Id));

        var tampered = Info("other", "beir-zip", new DatasetFile("https://example.test/x.zip", "x.zip", new string('0', 64)));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.EnsureAsync(tampered, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Checksum mismatch", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, "x.zip")));
    }

    [Fact]
    public void The_manifest_in_the_repository_is_valid()
    {
        var manifest = DatasetManifest.Load(DatasetManifest.FindDefault(AppContext.BaseDirectory));
        Assert.Contains(manifest.Datasets, d => d.Id == "scifact");
        Assert.All(manifest.Datasets.SelectMany(d => d.Files), f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
        Assert.Throws<KeyNotFoundException>(() => manifest.Get("nope"));
    }

    [Fact]
    public async Task Indexes_a_corpus_once_and_reuses_it()
    {
        using var client = QdrantFixture.ClientOrSkip();
        WriteBeirJsonl("tiny");
        var dataset = EvalDataset.Load(Info("tiny", "beir-jsonl"), _dir);
        var index = new CorpusIndex(client, new FixedSizeChunker(40, 5), new Embedder(new HashingEmbeddingGenerator(), EmbeddingProfile.For("t")));
        var ct = TestContext.Current.CancellationToken;
        var name = CorpusIndex.CollectionName(dataset, new FixedSizeChunker(40, 5), EmbeddingProfile.For("t"));
        try
        {
            var first = await index.EnsureAsync(dataset, cancellationToken: ct);
            var second = await index.EnsureAsync(dataset, cancellationToken: ct);
            var forced = await index.EnsureAsync(dataset, force: true, cancellationToken: ct);

            Assert.Equal(name, first.Collection);
            Assert.StartsWith("eval-tiny-fixed-40-5-t-", name, StringComparison.Ordinal);
            Assert.False(first.Reused);
            Assert.True(second.Reused);
            Assert.False(forced.Reused);
            Assert.Equal((2, first.Chunks), (second.Documents, second.Chunks));
            Assert.Equal((ulong)first.Chunks, await client.CountAsync(name, exact: true, cancellationToken: ct));
        }
        finally
        {
            await client.DeleteCollectionAsync(name, cancellationToken: CancellationToken.None);
        }
    }

    private sealed class StaticHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
