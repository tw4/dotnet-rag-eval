using RagEval.Evaluation.Experiments;
using RagEval.Evaluation.Judging;

namespace RagEval.Tests;

public sealed class AgreementTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rag-eval-agree-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AnswerQueryResult Query(string id, bool refused, Verdict? faithful, Verdict correct) =>
        new(id, $"question {id}", refused ? "refused" : $"answer, with \"quotes\"\nand a line {id}", refused, null, "reference",
            ["c#0"], ["passage one", "passage, two"], [1], true, null,
            faithful is null ? null : new JudgeVerdict(faithful.Value, "r"), new JudgeVerdict(correct, "r"), 1, 1);

    private static AnswerExperimentResult Result(params AnswerQueryResult[] queries) => new(
        new ExperimentConfig { Name = "x", Datasets = ["d"], Runs = [new RunConfig { Name = "run" }] }, "judge",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new RunEnvironment(null, null, "os", "cpu", "net", null, null, new Dictionary<string, string>()),
        [AnswerEvaluator.Summarize("d", new RunConfig { Name = "run" }, queries)], null);

    [Fact]
    public void Kappa_matches_the_textbook_example()
    {
        int[][] confusion = [[20, 0, 5], [0, 0, 0], [10, 0, 15]];
        Assert.Equal(0.4, Agreement.Kappa(confusion, (i, j) => i == j ? 1 : 0), 10);
        Assert.Equal(1, Agreement.Kappa([[3, 0, 0], [0, 2, 0], [0, 0, 5]], (i, j) => i == j ? 1 : 0), 10);

        var c = Agreement.Criterion("x", [(Verdict.Yes, Verdict.Yes), (Verdict.Yes, Verdict.Partial), (Verdict.No, Verdict.No), (Verdict.Partial, Verdict.Partial)]);
        Assert.Equal(0.75, c.Exact);
        Assert.Equal(0.75, c.Binary);
        Assert.True(c.WeightedKappa > c.Kappa);    // yes vs partial is only half wrong
        Assert.Equal(1, c.Confusion[2][1]);
        Assert.Equal(0.5, c.JudgeMean);
        Assert.Equal(0.625, c.HumanMean);
    }

    [Fact]
    public void Csv_round_trips_quotes_commas_and_line_breaks()
    {
        var rows = Csv.Parse("﻿a,\"b, \"\"c\"\"\nd\",e\r\nf,,\"\"\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b, \"c\"\nd", "e"], rows[0]);
        Assert.Equal(["f", "", ""], rows[1]);
    }

    [Fact]
    public void Samples_answered_questions_reproducibly_and_reads_labels_back()
    {
        var result = Result(Query("q1", false, Verdict.Yes, Verdict.Yes), Query("q2", true, null, Verdict.No),
            Query("q3", false, Verdict.Partial, Verdict.No), Query("q4", false, Verdict.No, Verdict.Partial));

        var sample = Agreement.Sample(result, 10);
        Assert.Equal(["q1", "q3", "q4"], sample.Select(s => s.Query.QueryId));       // refusals are not labelled
        Assert.Equal(sample, Agreement.Sample(result, 10));
        Assert.Equal(2, Agreement.Sample(result, 2).Count);

        var path = Path.Combine(_dir, "labels", "sample.csv");
        Agreement.WriteSheet(sample, path);
        var sheet = File.ReadAllText(path);
        Assert.DoesNotContain("\"r\"", sheet, StringComparison.Ordinal);               // no judge reasons or verdicts
        Assert.Contains("[2] passage, two", sheet, StringComparison.Ordinal);

        // Label the sheet as a person would: in the CSV columns.
        var rows = Csv.Parse(sheet).Select(r => r.ToArray()).ToList();
        string[][] labels = [["yes", "yes", "fine"], ["no", "no", ""], ["no", "", "unsure about correctness"]];
        for (var i = 0; i < 3; i++)
        {
            (rows[i + 1][5], rows[i + 1][6], rows[i + 1][7]) = (labels[i][0], labels[i][1], labels[i][2]);
        }

        File.WriteAllLines(path, rows.Select(r => string.Join(',', r.Select(c => "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""))));
        var read = Agreement.ReadLabels(path);
        Assert.Equal(3, read.Count);
        Assert.Null(read[2].Correctness);

        var agreement = Agreement.Compute(result, read);
        var faithfulness = agreement.Single(a => a.Criterion == "faithfulness");
        Assert.Equal(3, faithfulness.Items);
        Assert.Equal(2 / 3.0, faithfulness.Exact, 10);              // q3: judge partial, human no
        Assert.Equal(2, agreement.Single(a => a.Criterion == "correctness").Items);
        Assert.Contains("| faithfulness | 3 |", Agreement.Markdown(agreement, "judge"), StringComparison.Ordinal);

        File.WriteAllText(path, "id,faithfulness,correctness\nd/run/q9,yes,yes\n");
        Assert.Throws<InvalidDataException>(() => Agreement.Compute(result, Agreement.ReadLabels(path)));
        File.WriteAllText(path, "id,faithfulness,correctness\nd/run/q1,maybe,yes\n");
        Assert.Throws<InvalidDataException>(() => Agreement.ReadLabels(path));
    }
}
