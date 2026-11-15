using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Indexing;
using RagEval.Evaluation.Metrics;

namespace RagEval.Tests;

public class MetricsTests
{
    // Judgements and rankings with values computed by pytrec_eval 0.5 (the trec_eval behind BEIR):
    //   RelevanceEvaluator(qrels, {"ndcg_cut.10", "recall.5", "recall.10", "recip_rank"})
    private static readonly Dictionary<string, Dictionary<string, int>> Qrels = new()
    {
        ["q1"] = new() { ["d1"] = 1, ["d3"] = 1 },
        ["q2"] = new() { ["d2"] = 2, ["d5"] = 1, ["d9"] = 0 },
        ["q3"] = new() { ["d7"] = 1 },
        ["q4"] = new()
        {
            ["d1"] = 3,
            ["d2"] = 2,
            ["d3"] = 1,
            ["d4"] = 1,
            ["d5"] = 2,
            ["d6"] = 1,
            ["d7"] = 1,
            ["d8"] = 1,
            ["d9"] = 1,
            ["d10"] = 1,
            ["d11"] = 2,
            ["d12"] = 1,
        },
    };

    private static readonly Dictionary<string, string[]> Runs = new()
    {
        ["q1"] = ["d2", "d1", "d4", "d3", "d5"],
        ["q2"] = ["d9", "d5", "d8", "d2"],
        ["q3"] = ["d1", "d2", "d3"],
        ["q4"] = ["d12", "d1", "x1", "d5", "d2", "x2", "d11", "x3", "d3", "d4", "d6", "d7"],
    };

    [Theory]
    [InlineData("q1", 0.6509209298, 1.0, 1.0, 0.5)]
    [InlineData("q2", 0.5672074170, 1.0, 1.0, 0.5)]
    [InlineData("q3", 0.0, 0.0, 0.0, 0.0)]
    [InlineData("q4", 0.7136941711, 0.3333333333, 0.5833333333, 1.0)]
    public void Match_trec_eval(string query, double ndcg10, double recall5, double recall10, double reciprocalRank)
    {
        var ranking = Runs[query];
        var judgements = Qrels[query];

        Assert.Equal(ndcg10, RetrievalMetrics.NdcgAt(ranking, judgements, 10), 9);
        Assert.Equal(recall5, RetrievalMetrics.RecallAt(ranking, judgements, 5), 9);
        Assert.Equal(recall10, RetrievalMetrics.RecallAt(ranking, judgements, 10), 9);
        Assert.Equal(reciprocalRank, RetrievalMetrics.ReciprocalRankAt(ranking, judgements, int.MaxValue), 9);   // trec_eval: no cut-off
    }

    [Fact]
    public void Edge_cases()
    {
        Dictionary<string, int> none = new() { ["d1"] = 0 };
        Assert.Equal(0, RetrievalMetrics.NdcgAt(["d1"], none, 10));
        Assert.Equal(0, RetrievalMetrics.RecallAt(["d1"], none, 10));
        Assert.Equal(0, RetrievalMetrics.RecallAt([], Qrels["q1"], 10));
        Assert.Equal(1.0, RetrievalMetrics.NdcgAt(["d1", "d3"], Qrels["q1"], 10));                  // ideal order
        Assert.Equal(0, RetrievalMetrics.ReciprocalRankAt(Runs["q1"], Qrels["q1"], 1));             // cut-off before the first hit
    }

    private static RetrievedChunk Hit(string doc, int index, string text, double score) =>
        new(new Chunk(doc, index, text, 0, text.Length, new SourceSpan(0, text.Length, 1, 1)), score, null, null);

    [Fact]
    public void A_document_ranks_by_its_best_chunk()
    {
        var ranking = RetrievalMetrics.DocumentRanking(
        [
            Hit("a", 0, "x", 0.5), Hit("b", 0, "x", 0.9), Hit("a", 1, "x", 0.95), Hit("c", 0, "x", 0.1), Hit("b", 3, "x", 0.2),
        ]);
        Assert.Equal(["a", "b", "c"], ranking);
    }

    [Fact]
    public void Answer_hit_needs_the_right_document_and_the_answer_text()
    {
        Dictionary<string, int> judgements = new() { ["doc"] = 1 };
        const string span = "yaklaşık %25'i, diyabet teşhisi konulduğunda";
        var right = Hit("doc", 2, "… Hastaların Yaklaşık %25'i,\ndiyabet  teşhisi konulduğunda ketoasidoz …", 0.7);
        var wrongDocument = Hit("other", 0, "yaklaşık %25'i, diyabet teşhisi konulduğunda", 0.9);
        var cut = Hit("doc", 1, "yaklaşık %25'i, diyabet", 0.8);

        Assert.True(RetrievalMetrics.AnswerHitAt([wrongDocument, cut, right], judgements, span, 3));
        Assert.False(RetrievalMetrics.AnswerHitAt([wrongDocument, cut, right], judgements, span, 2));   // answer split across chunks
        Assert.False(RetrievalMetrics.AnswerHitAt([right], judgements, "", 3));
    }
}
