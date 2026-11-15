using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Experiments;

namespace RagEval.Tests;

public sealed class ComparisonTests
{
    private static ExperimentResult Result(params (string Run, double Ndcg, int Queries)[] runs) => new(
        new ExperimentConfig { Name = "smoke", Datasets = ["d"], Runs = [.. runs.Select(r => new RunConfig { Name = r.Run })] },
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new RunEnvironment(null, null, "os", "cpu", "net", null, null, new Dictionary<string, string>()),
        [.. runs.Select(r => ExperimentRunner.Summarize("d", new RunConfig { Name = r.Run }, new IndexResult("c", 1, 1, 1, TimeSpan.Zero, true),
            [.. Enumerable.Range(0, r.Queries).Select(i => new QueryResult($"q{i}", r.Ndcg, 1, 1, 1, null, null, [], 1, 1))]))],
        null);

    [Fact]
    public void Flags_drops_beyond_the_tolerance_only()
    {
        var changes = Comparison.Compare(Result(("dense", 0.700, 10), ("bm25", 0.800, 10)), Result(("dense", 0.695, 10), ("bm25", 0.750, 10)));

        Assert.Equal(8, changes.Count);                          // ndcg, recall@10, recall@100, mrr for both runs
        var regression = Assert.Single(Comparison.Regressions(changes, 0.01));
        Assert.Equal(("bm25", "ndcg@10"), (regression.Run, regression.Metric));
        Assert.Equal(-0.05, regression.Delta, 10);
        Assert.Contains("⚠ regression", Comparison.Markdown(changes, 0.01), StringComparison.Ordinal);
        Assert.Empty(Comparison.Regressions(changes, 0.1));
    }

    [Fact]
    public void Refuses_results_that_do_not_match_the_reference()
    {
        Assert.Throws<InvalidDataException>(() => Comparison.Compare(Result(("dense", 0.7, 10)), Result(("hybrid", 0.7, 10))));
        Assert.Throws<InvalidDataException>(() => Comparison.Compare(Result(("dense", 0.7, 10)), Result(("dense", 0.7, 5))));
    }
}
