using RagEval.Core.Answering;
using RagEval.Core.Indexing;
using RagEval.Core.Telemetry;
using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Experiments;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class LatencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rag-eval-lat-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Recorder_groups_stages_by_request_and_reads_token_counts()
    {
        using var recorder = new StageRecorder();
        for (var i = 0; i < 2; i++)
        {
            using var root = Stages.Start(Stages.Answer);
            using (Stages.Start(Stages.EmbedQuery))
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            using (var generate = Stages.Start(Stages.Generate))
            {
                generate?.SetTag("output_tokens", 40);
            }
        }

        using (Stages.Start(Stages.Load))
        {
            // a span outside any answer: its own request
        }

        var requests = recorder.Requests(Stages.Answer);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.True(r[Stages.EmbedQuery] >= 4));
        Assert.All(requests, r => Assert.True(r[Stages.Answer] >= r[Stages.EmbedQuery]));
        Assert.All(requests, r => Assert.Equal(40, r[Stages.Generate + StageRecorder.TokensSuffix]));
        Assert.Single(recorder.Requests(Stages.Load));

        recorder.Clear();
        Assert.Empty(recorder.Requests(Stages.Answer));
    }

    [Fact]
    public void Derived_stages_separate_search_from_rerank_and_give_tokens_per_second()
    {
        var derived = LatencyBench.WithDerived(new Dictionary<string, double>
        {
            [Stages.Retrieve] = 300,
            [Stages.Rerank] = 250,
            [Stages.Generate] = 2000,
            [Stages.Generate + StageRecorder.TokensSuffix] = 50,
        });
        Assert.Equal(50, derived["query.search"]);
        Assert.Equal(25, derived["tokens_per_second"]);

        var summary = LatencyBench.Summarize([derived, LatencyBench.WithDerived(new Dictionary<string, double> { [Stages.Retrieve] = 10 })],
            LatencyBench.AnswerStages);
        Assert.Equal(2, summary.Single(s => s.Stage == "query.search").Count);
        Assert.Equal(1, summary.Single(s => s.Stage == Stages.Rerank).Count);
        Assert.DoesNotContain(summary, s => s.Stage == Stages.EmbedQuery);
    }

    [Fact]
    public async Task Answerer_marks_its_stages_and_skips_embedding_for_sparse_retrievers()
    {
        using var recorder = new StageRecorder();
        var chat = new ScriptedChatClient("Two [1].");
        var chunk = new RetrievedChunk(new Core.Chunking.Chunk("d", 0, "Mars has two moons.", 0, 19, new Core.Documents.SourceSpan(0, 19, 1, 1)), 1, null, null);
        var answerer = new Answerer(null, new SparseOnly(chunk), chat, new AnsweringOptions());

        var answer = await answerer.AskAsync("How many moons?", TestContext.Current.CancellationToken);

        Assert.False(answer.Refused);
        var request = Assert.Single(recorder.Requests(Stages.Answer));
        Assert.Contains(Stages.Retrieve, request.Keys);
        Assert.Contains(Stages.Generate, request.Keys);
        Assert.DoesNotContain(Stages.EmbedQuery, request.Keys);
        Assert.Equal(2, request[Stages.Generate + StageRecorder.TokensSuffix]);

        var dense = new Answerer(null, new DenseRetriever(null!), chat, new AnsweringOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => dense.AskAsync("q", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Benchmark_times_answers_and_ingestion_end_to_end()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var info = TinyDataset.Write(_dir);
        Task<EvalDataset> Load(string id, CancellationToken ct) => Task.FromResult(EvalDataset.Load(info, _dir));
        var runner = new ExperimentRunner(qdrant, new(), Load) { EmbeddingsFor = _ => new HashingEmbeddingGenerator() };
        var bench = new LatencyBench(qdrant, new(), runner, Load) { ChatFor = _ => new ScriptedChatClient("Two moons [1].") };
        var config = new LatencyConfig
        {
            Name = "unit-latency",
            Dataset = "tiny",
            Queries = 2,
            WarmUp = 0,
            Documents = 2,
            Runs =
            [
                new RunConfig { Name = "dense", Chunking = new ChunkingConfig { Size = 70, Overlap = 10 }, Embedding = new EmbeddingConfig { Model = "hash" } },
                new RunConfig { Name = "bm25", Chunking = new ChunkingConfig { Size = 70, Overlap = 10 }, Retrieval = new RetrievalConfig { Strategy = "sparse" } },
            ],
        };

        var collections = new List<string>();
        try
        {
            var result = await bench.RunAsync(config, TestContext.Current.CancellationToken);
            collections.AddRange(await CollectionsAsync(qdrant, "eval-tiny-fixed-70-10-"));   // distinct from other tests running in parallel

            var dense = result.Runs[0];
            Assert.Equal(2, dense.Answers);
            Assert.Equal(0, dense.Refusals);
            Assert.Equal(2, dense.Answer.Single(s => s.Stage == Stages.Answer).Count);
            Assert.Contains(dense.Answer, s => s.Stage == Stages.EmbedQuery);
            Assert.Contains(dense.Answer, s => s.Stage == "tokens_per_second");
            Assert.Equal(2, dense.Ingestion.Single(s => s.Stage == Stages.Document).Count);   // the first document is not timed
            Assert.Empty(result.Runs[1].Ingestion);
            Assert.DoesNotContain(result.Runs[1].Answer, s => s.Stage == Stages.EmbedQuery);
            Assert.Empty(await CollectionsAsync(qdrant, "bench-"));

            var (json, markdown) = LatencyBench.Write(result, Path.Combine(_dir, "out"));
            var md = await File.ReadAllTextAsync(markdown, TestContext.Current.CancellationToken);
            Assert.Contains("## Answering a question (ms)", md, StringComparison.Ordinal);
            Assert.Contains("| bm25 | – |", md, StringComparison.Ordinal);
            Assert.True(File.Exists(json));
        }
        finally
        {
            foreach (var collection in collections)
            {
                await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
            }
        }
    }

    [Fact]
    public void The_committed_latency_benchmark_is_valid()
    {
        var folder = Path.Combine(Path.GetDirectoryName(DatasetManifest.FindDefault(AppContext.BaseDirectory))!, "..", "experiments");
        Assert.All(Directory.GetFiles(folder, "latency*.json"), f => LatencyConfig.Load(f));
    }

    private static async Task<List<string>> CollectionsAsync(Qdrant.Client.QdrantClient qdrant, string prefix) =>
        [.. (await qdrant.ListCollectionsAsync(TestContext.Current.CancellationToken)).Where(c => c.StartsWith(prefix, StringComparison.Ordinal))];

    private sealed class SparseOnly(RetrievedChunk chunk) : IRetriever
    {
        public string Name => "bm25";

        public bool NeedsQueryVector => false;

        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RetrievedChunk>>([chunk]);
    }
}
