using OllamaSharp;
using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Indexing;
using RagEval.Core.Reranking;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class RerankingTests
{
    private static RetrievedChunk Hit(string doc, double score, string? section = null) =>
        new(new Chunk(doc, 0, $"text of {doc}", 0, 10, new SourceSpan(0, 10, 1, 1, null), section), score, null, null);

    [Fact]
    public void Score_is_the_yes_probability_against_no()
    {
        Assert.Equal(0.5, YesNoReranker.Score([("yes", -1), ("no", -1)]), 10);
        Assert.Equal(1 / (1 + Math.Exp(-2)), YesNoReranker.Score([("no", -3), ("Yes", -0.1), ("yes", -1)]), 10);
        Assert.Equal(1, YesNoReranker.Score([("yes", -0.1), ("No", -5)]));
        Assert.Equal(0, YesNoReranker.Score([("no", -0.1)]));
        Assert.Equal(0.5, YesNoReranker.Score([("maybe", -0.1)]));
    }

    [Fact]
    public void Prompt_follows_the_model_card_and_cuts_long_passages()
    {
        var prompt = YesNoReranker.Prompt(YesNoReranker.DefaultInstruction, "q?", "passage");
        Assert.StartsWith("<|im_start|>system\nJudge whether the Document", prompt, StringComparison.Ordinal);
        Assert.Contains("<Instruct>: Given a web search query, retrieve relevant passages that answer the query\n<Query>: q?\n<Document>: passage<|im_end|>",
            prompt, StringComparison.Ordinal);
        Assert.EndsWith("<|im_start|>assistant\n<think>\n\n</think>\n\n", prompt, StringComparison.Ordinal);

        var long_ = YesNoReranker.Prompt("i", "q", new string('x', 10_000));
        Assert.Contains(new string('x', YesNoReranker.MaxPassageLength) + "<|im_end|>", long_, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', YesNoReranker.MaxPassageLength + 1), long_, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_reorders_the_head_and_keeps_the_tail_below_it()
    {
        RetrievedChunk[] retrieved = [Hit("a", 0.9), Hit("b", 0.8), Hit("c", 0.7), Hit("d", 0.6), Hit("e", 0.5)];
        var reranked = Rerank.Apply(retrieved, [0.1, 0.9, 0.1], candidates: 3);

        Assert.Equal(["b", "a", "c", "d", "e"], reranked.Select(r => r.Chunk.DocumentId));   // a and c tie: retrieval order
        Assert.Equal([0.9, 0.1, 0.1, -1, -2], reranked.Select(r => r.Score));
        Assert.Equal(["b", "a", "c", "d", "e"], RagEval.Evaluation.Metrics.RetrievalMetrics.DocumentRanking(reranked));
        Assert.Throws<ArgumentException>(() => Rerank.Apply(retrieved, [0.5], 3));
        Assert.Equal(2, Rerank.Apply([Hit("a", 1), Hit("b", 1)], [0.2, 0.3], 20).Count);   // fewer than the candidates
    }

    [Fact]
    public async Task Reranking_retriever_fetches_candidates_and_reads_sections()
    {
        var inner = new ListRetriever([Hit("a", 0.9), Hit("b", 0.8, "Guide > Install"), Hit("c", 0.7)]);
        var reranker = new LookupReranker(new() { ["text of c"] = 0.9, ["Guide > Install\ntext of b"] = 0.5 });
        var retriever = new RerankingRetriever(inner, reranker, candidates: 3);

        var hits = await retriever.RetrieveAsync("q", default, 2, TestContext.Current.CancellationToken);

        Assert.Equal(3, inner.AskedFor);
        Assert.Equal(["c", "b"], hits.Select(h => h.Chunk.DocumentId));
        Assert.Equal("dense > rerank(lookup, 3)", retriever.Name);
    }

    [Fact]
    public async Task Qwen3_reranker_in_ollama_prefers_the_answering_passage()
    {
        OllamaFixture.SkipUnlessModel("qwen3-reranker:0.6b");
        using var client = new OllamaApiClient(OllamaFixture.Url);
        var reranker = new YesNoReranker(client, "qwen3-reranker:0.6b");

        var scores = await reranker.ScoreAsync("What is the capital of France?",
            ["Bananas are rich in potassium.", "Paris is the capital and largest city of France.", "Fransa'nın başkenti Paris'tir."],
            TestContext.Current.CancellationToken);

        Assert.True(scores[0] < 0.1, $"irrelevant: {scores[0]}");
        Assert.True(scores[1] > 0.9, $"relevant: {scores[1]}");
        Assert.True(scores[2] > 0.9, $"relevant, Turkish: {scores[2]}");
    }

    private sealed class ListRetriever(IReadOnlyList<RetrievedChunk> hits) : IRetriever
    {
        public int AskedFor { get; private set; }

        public string Name => "dense";

        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
            CancellationToken cancellationToken = default)
        {
            AskedFor = limit;
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>([.. hits.Take(limit)]);
        }
    }

    internal sealed class LookupReranker(Dictionary<string, double> scores) : IReranker
    {
        public string Name => "lookup";

        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<double>>([.. passages.Select(p => scores.GetValueOrDefault(p))]);
    }
}
