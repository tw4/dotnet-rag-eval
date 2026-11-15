using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Experiments;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class ExperimentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rag-eval-exp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Experiment_files_allow_comments_and_reject_mistakes()
    {
        var config = ExperimentConfig.Load(Write("ok.json", """
            {
              // comment
              "name": "x", "datasets": ["scifact"],
              "runs": [ { "name": "a", "chunking": { "size": 500, "overlap": 50 } }, ],
            }
            """));
        Assert.Equal(("fixed", 500, 50, "nomic-embed-text", "dense", 100),
            (config.Runs[0].Chunking.Strategy, config.Runs[0].Chunking.Size, config.Runs[0].Chunking.Overlap,
             config.Runs[0].Embedding.Model, config.Runs[0].Retrieval.Strategy, config.Runs[0].Retrieval.Depth));

        Assert.ThrowsAny<Exception>(() => ExperimentConfig.Load(Write("typo.json",
            """{ "name": "x", "datasets": ["s"], "runs": [ { "name": "a", "chunkin": {} } ] }""")));
        Assert.Throws<InvalidDataException>(() => ExperimentConfig.Load(Write("dup.json",
            """{ "name": "x", "datasets": ["s"], "runs": [ { "name": "a" }, { "name": "a" } ] }""")));
        Assert.Throws<InvalidDataException>(() => ExperimentConfig.Load(Write("empty.json",
            """{ "name": "x", "datasets": [], "runs": [ { "name": "a" } ] }""")));
    }

    [Fact]
    public void The_committed_experiments_are_valid()
    {
        var folder = Path.Combine(Path.GetDirectoryName(DatasetManifest.FindDefault(AppContext.BaseDirectory))!, "..", "experiments");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.NotEmpty(files);
        Assert.All(files, f => ExperimentConfig.Load(f));
    }

    [Fact]
    public void Percentiles_and_bootstrap_intervals()
    {
        Assert.Equal(2.5, Statistics.Percentile([1, 2, 3, 4], 50));
        Assert.Equal(3.85, Statistics.Percentile([1, 2, 3, 4], 95), 10);
        Assert.Equal(1, Statistics.Percentile([1], 95));

        double[] values = [.. Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? 1.0 : 0.0)];
        var (low, high) = Statistics.BootstrapCi95(values);
        Assert.InRange(low, 0.42, 0.5);
        Assert.InRange(high, 0.5, 0.58);
        Assert.Equal((low, high), Statistics.BootstrapCi95(values));   // fixed seed: reproducible
    }

    private DatasetInfo WriteTinyDataset()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "tiny", "qrels"));
        Write("tiny/corpus.jsonl", """
            {"_id": "pets", "title": "Pets", "text": "Cats sleep most of the day. Dogs enjoy long walks in the park."}
            {"_id": "space", "title": "Space", "text": "The moon orbits the earth. Mars has two small moons called Phobos and Deimos."}
            {"_id": "food", "title": "Food", "text": "Bread is baked from flour, water and yeast."}
            """);
        Write("tiny/queries.jsonl", """
            {"_id": "q1", "text": "how many moons does mars have", "answer_span": "Mars has two small moons"}
            {"_id": "q2", "text": "what do dogs enjoy", "answer_span": "Dogs enjoy long walks"}
            """);
        Write("tiny/qrels/test.jsonl", """
            {"query-id": "q1", "corpus-id": "space", "score": 1}
            {"query-id": "q2", "corpus-id": "pets", "score": 1}
            """);
        return new DatasetInfo("tiny", "tiny", "en", "t", "x", "x", "x", "beir-jsonl", "test", [], new DatasetSize(3, 2, 2));
    }

    [Fact]
    public async Task Runs_sparse_and_hybrid_retrieval()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var info = WriteTinyDataset();
        var chunking = new ChunkingConfig { Size = 60, Overlap = 10 };
        var experiment = new ExperimentConfig
        {
            Name = "unit-hybrid",
            Datasets = ["tiny"],
            Runs =
            [
                new RunConfig { Name = "bm25", Chunking = chunking, Retrieval = new RetrievalConfig { Strategy = "sparse", Analyzer = "prefix-4" } },
                new RunConfig { Name = "hybrid", Chunking = chunking, Embedding = new EmbeddingConfig { Model = "hash" },
                    Retrieval = new RetrievalConfig { Strategy = "hybrid", Analyzer = "prefix-4" } },
                new RunConfig { Name = "bm25 + rerank", Chunking = chunking,
                    Retrieval = new RetrievalConfig { Strategy = "sparse", Analyzer = "prefix-4", Depth = 2 },
                    Rerank = new RerankConfig { Model = "overlap", Candidates = 5 } },
            ],
        };
        var embedded = new List<string>();
        var runner = new ExperimentRunner(qdrant, new(), (_, _) => Task.FromResult(EvalDataset.Load(info, _dir)))
        {
            EmbeddingsFor = model =>
            {
                embedded.Add(model);
                return new HashingEmbeddingGenerator();
            },
            RerankerFor = config => new WordOverlapReranker(),
        };
        var collections = new List<string>();
        try
        {
            var result = await runner.RunAsync(experiment, TestContext.Current.CancellationToken);
            collections.AddRange(result.Runs.SelectMany(r => r.Index.Collection.Split(" + ")).Distinct());

            Assert.Equal(["hash"], embedded);                       // the sparse run embeds nothing
            var (sparse, hybrid) = (result.Runs[0], result.Runs[1]);
            Assert.Equal(1.0, sparse.Metrics["recall@10"]);
            Assert.Equal("space", sparse.PerQuery[0].Top10Documents[0]);
            Assert.Equal(1.0, hybrid.Metrics["ndcg@10"]);
            Assert.Equal(0, sparse.Index.Dimensions);
            Assert.True(sparse.Index.VectorBytes > 0);
            Assert.Contains(" + ", hybrid.Index.Collection, StringComparison.Ordinal);
            Assert.Equal(sparse.Index.Chunks, hybrid.Index.Chunks);  // same chunks in both indexes
            Assert.True(hybrid.Index.VectorBytes > sparse.Index.VectorBytes);

            var reranked = result.Runs[2];
            Assert.Equal(1.0, reranked.Metrics["recall@10"]);
            Assert.InRange(reranked.PerQuery[0].Top10Documents.Count, 1, 2);   // depth 2 (chunks) after reranking 5 candidates
            Assert.NotNull(reranked.Rerank);
            Assert.Null(sparse.Rerank);

            var again = await runner.RunAsync(experiment, TestContext.Current.CancellationToken);
            Assert.All(again.Runs, r => Assert.True(r.Index.Reused));
            Assert.Equal(sparse.Metrics["ndcg@10"], again.Runs[0].Metrics["ndcg@10"]);

            var (_, markdown) = ReportWriter.Write(result, Path.Combine(_dir, "out-hybrid"));
            var md = await File.ReadAllTextAsync(markdown, TestContext.Current.CancellationToken);
            Assert.Contains("| bm25 | **1.000** |", md, StringComparison.Ordinal);
            Assert.Contains("Rerank p50 / p95 (ms) |", md, StringComparison.Ordinal);
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
    public async Task Runs_an_experiment_end_to_end_and_writes_reports()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var info = WriteTinyDataset();
        var experiment = new ExperimentConfig
        {
            Name = "unit",
            Datasets = ["tiny"],
            Runs = [new RunConfig { Name = "small chunks", Chunking = new ChunkingConfig { Size = 60, Overlap = 10 }, Embedding = new EmbeddingConfig { Model = "hash" } }],
        };
        var runner = new ExperimentRunner(qdrant, new(), (_, _) => Task.FromResult(EvalDataset.Load(info, _dir)))
        {
            EmbeddingsFor = _ => new HashingEmbeddingGenerator(),
        };
        var collection = "";
        try
        {
            var result = await runner.RunAsync(experiment, TestContext.Current.CancellationToken);
            var run = Assert.Single(result.Runs);
            collection = run.Index.Collection;

            Assert.Equal(2, run.Queries);
            Assert.Equal(1.0, run.Metrics["recall@10"]);
            Assert.Equal(1.0, run.Metrics["ndcg@10"]);           // the hashing embedder ranks these right
            Assert.Equal(1.0, run.Metrics["answer_hit@5"]);
            Assert.Equal("space", run.PerQuery[0].Top10Documents[0]);
            Assert.Equal(3, run.PerQuery[0].Top10Documents.Count);
            Assert.NotNull(result.Environment.GitCommit);

            var (json, markdown) = ReportWriter.Write(result, Path.Combine(_dir, "out"));
            var back = ReportWriter.Read(json);
            Assert.Equal(run.Metrics["ndcg@10"], back.Runs[0].Metrics["ndcg@10"]);
            var md = await File.ReadAllTextAsync(markdown, TestContext.Current.CancellationToken);
            Assert.Contains("## tiny (2 queries)", md, StringComparison.Ordinal);
            Assert.Contains("| small chunks | **1.000** |", md, StringComparison.Ordinal);
            Assert.Contains("Vectors (MB)", md, StringComparison.Ordinal);
            Assert.Contains("Answer hit@5", md, StringComparison.Ordinal);
        }
        finally
        {
            if (collection.Length > 0)
            {
                await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
            }
        }
    }

    /// <summary>Scores a passage by the share of query words it contains.</summary>
    private sealed class WordOverlapReranker : RagEval.Core.Reranking.IReranker
    {
        public string Name => "overlap";

        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
        {
            var words = query.ToLowerInvariant().Split(' ');
            return Task.FromResult<IReadOnlyList<double>>(
                [.. passages.Select(p => words.Count(w => p.Contains(w, StringComparison.OrdinalIgnoreCase)) / (double)words.Length)]);
        }
    }
}
