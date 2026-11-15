using Microsoft.Extensions.AI;
using RagEval.Core.Answering;
using RagEval.Evaluation.Datasets;
using RagEval.Evaluation.Experiments;
using RagEval.Evaluation.Judging;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public sealed class JudgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rag-eval-judge-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Parses_verdicts_and_rejects_anything_else()
    {
        Assert.Equal(new JudgeVerdict(Verdict.Yes, "All supported."), AnswerJudge.Parse("""{"reason": "All supported.", "verdict": "yes"}"""));
        Assert.Equal(Verdict.Partial, AnswerJudge.Parse("Sure:\n```json\n{\"reason\": \"x\", \"verdict\": \" Partial \"}\n```").Verdict);
        Assert.Equal(0.5, AnswerJudge.Parse("""{"reason": "", "verdict": "partial"}""").Score);
        Assert.Equal(0, AnswerJudge.Parse("""{"verdict": "no"}""").Score);
        Assert.Throws<FormatException>(() => AnswerJudge.Parse("""{"reason": "x", "verdict": "maybe"}"""));
        Assert.Throws<FormatException>(() => AnswerJudge.Parse("yes"));
    }

    [Fact]
    public async Task Asks_each_question_separately_with_a_json_schema()
    {
        var chat = new ScriptedChatClient("""{"reason": "ok", "verdict": "yes"}""");
        var judge = new AnswerJudge(chat, new ChatOptions { Temperature = 0 });

        var faithful = await judge.FaithfulnessAsync("How many moons?", "Two [1].", ["Mars has two moons.", "Earth has one."],
            TestContext.Current.CancellationToken);
        var correct = await judge.CorrectnessAsync("How many moons?", "Two [1].", "Mars has two small moons", TestContext.Current.CancellationToken);

        Assert.Equal((Verdict.Yes, Verdict.Yes), (faithful.Verdict, correct.Verdict));
        var first = chat.Calls[0][1].Text;
        Assert.Contains("[1] Mars has two moons.\n\n[2] Earth has one.", first, StringComparison.Ordinal);
        Assert.Contains("supported by the passages", first, StringComparison.Ordinal);
        Assert.Contains("Reference passage: Mars has two small moons", chat.Calls[1][1].Text, StringComparison.Ordinal);
        Assert.All(chat.Options, o => Assert.IsType<ChatResponseFormatJson>(o!.ResponseFormat));
        Assert.All(chat.Options, o => Assert.Equal(0, o!.Temperature));
    }

    [Fact]
    public async Task Evaluates_answers_end_to_end_and_counts_refusals_as_wrong()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var info = TinyDataset.Write(_dir);
        Task<EvalDataset> Load(string id, CancellationToken ct) => Task.FromResult(EvalDataset.Load(info, _dir));
        var runner = new ExperimentRunner(qdrant, new(), Load) { EmbeddingsFor = _ => new HashingEmbeddingGenerator() };

        // The answering model answers the moon question and refuses the other; the judge finds answers
        // partly faithful and correct.
        var answers = new ScriptedChatClient(m => m[1].Text.Contains("moons", StringComparison.Ordinal)
            ? "Mars has two small moons [1]." : Answerer.RefusalSentence);
        var judge = new ScriptedChatClient(m => m[1].Text.StartsWith("Reference passage", StringComparison.Ordinal)
            ? """{"reason": "matches", "verdict": "yes"}""" : """{"reason": "detail missing", "verdict": "partial"}""");
        var evaluator = new AnswerEvaluator(qdrant, new(), runner, Load) { ChatFor = model => model == "judge" ? judge : answers };
        var experiment = new ExperimentConfig
        {
            Name = "unit-answers",
            Datasets = ["tiny"],
            Judge = new JudgeConfig { Model = "judge" },
            Runs = [new RunConfig { Name = "bm25", Chunking = new ChunkingConfig { Size = 80, Overlap = 10 }, Retrieval = new RetrievalConfig { Strategy = "sparse" } }],
        };

        var collections = new List<string>();
        try
        {
            var result = await evaluator.RunAsync(experiment, TestContext.Current.CancellationToken);
            collections.AddRange((await qdrant.ListCollectionsAsync(TestContext.Current.CancellationToken))
                .Where(c => c.StartsWith("eval-tiny-fixed-80-10-", StringComparison.Ordinal)));

            var run = Assert.Single(result.Runs);
            Assert.Equal(2, run.Queries);
            Assert.Equal(0.5, run.Metrics["answered"]);
            Assert.Equal(0.5, run.Metrics["faithfulness"]);       // partial, over the one answer
            Assert.Equal(0.5, run.Metrics["correctness"]);        // yes and a refusal
            Assert.Equal(1.0, run.Metrics["context_has_answer"]);
            Assert.Equal(1.0, run.Metrics["cited_has_answer"]);
            var moons = run.PerQuery.Single(q => q.QueryId == "q1");
            Assert.Equal([1], moons.Cited);
            Assert.Equal("Mars has two small moons", moons.Reference);
            Assert.NotEmpty(moons.Context);
            var refused = run.PerQuery.Single(q => q.QueryId == "q2");
            Assert.True(refused.Refused);
            Assert.Null(refused.Faithfulness);
            Assert.Equal(2, judge.Calls.Count);                 // two verdicts for the one answer, none for the refusal
            Assert.Single(judge.Calls, c => c[1].Text.StartsWith("Reference passage", StringComparison.Ordinal));

            var (json, markdown) = AnswerEvaluator.Write(result, Path.Combine(_dir, "out"));
            Assert.Equal(0.5, AnswerEvaluator.Read(json).Runs[0].Metrics["correctness"]);
            Assert.Contains("| bm25 | **0.500** |", await File.ReadAllTextAsync(markdown, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var collection in collections)
            {
                await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
            }
        }
    }
}
