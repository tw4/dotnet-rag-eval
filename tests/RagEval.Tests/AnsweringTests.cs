using RagEval.Core.Answering;
using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public class AnsweringTests
{
    private sealed class FixedRetriever(params RetrievedChunk[] chunks) : IRetriever
    {
        public string Name => "fixed";

        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RetrievedChunk>>([.. chunks.Take(limit)]);
    }

    private static RetrievedChunk Hit(string doc, int index, string text, double score, int line) =>
        new(new Chunk(doc, index, text, 0, text.Length, new SourceSpan(10 * index, (10 * index) + text.Length, line, line + 1), "Section"),
            score, "Title", doc);

    private static Answerer Make(ScriptedChatClient chat, double minScore = 0.5, params RetrievedChunk[] hits) =>
        new(new Embedder(new HashingEmbeddingGenerator(), EmbeddingProfile.For("x")), new FixedRetriever(hits), chat,
            new AnsweringOptions { TopK = 5, MinScore = minScore });

    [Fact]
    public async Task Cites_the_chunks_the_answer_refers_to()
    {
        var chat = new ScriptedChatClient("Mars has two moons [2]. They are small [2][3]. Earth has one [1, 2]. Bogus [9].");
        var answerer = Make(chat, 0.5,
            Hit("earth.md", 0, "The moon orbits the earth.", 0.9, 3),
            Hit("mars.md", 0, "Mars has two moons, Phobos and Deimos.", 0.8, 7),
            Hit("mars.md", 1, "Both moons are small.", 0.7, 12));

        var answer = await answerer.AskAsync("How many moons does Mars have?", TestContext.Current.CancellationToken);

        Assert.False(answer.Refused);
        Assert.Equal([2, 3, 1], answer.Citations.Select(c => c.Number));   // first appearance order, [9] ignored
        var mars = answer.Citations[0];
        Assert.Equal(("mars.md", "mars.md#0", 7, 8, "Section", "Title"), (mars.DocumentId, mars.ChunkId, mars.StartLine, mars.EndLine, mars.Section, mars.Title));
        Assert.Equal(3, answer.Retrieved.Count);

        var prompt = chat.Calls.Single()[1].Text;
        Assert.Contains("[1] (earth.md: Title — Section)", prompt, StringComparison.Ordinal);
        Assert.Contains("[3] (mars.md: Title — Section)\nBoth moons are small.", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Question: How many moons does Mars have?", prompt, StringComparison.Ordinal);
        Assert.Contains(Answerer.RefusalSentence, chat.Calls.Single()[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_without_calling_the_model_when_nothing_is_relevant()
    {
        var chat = new ScriptedChatClient("should not be called");
        var answerer = Make(chat, 0.5, Hit("a.md", 0, "unrelated", 0.3, 1));

        var answer = await answerer.AskAsync("What is the capital of Peru?", TestContext.Current.CancellationToken);

        Assert.True(answer.Refused);
        Assert.Equal(Answerer.NothingRetrieved, answer.RefusalReason);
        Assert.Empty(answer.Citations);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task Refuses_when_the_model_finds_no_answer_in_the_context()
    {
        var chat = new ScriptedChatClient("<think>Let me look... no.</think>\n" + Answerer.RefusalSentence);
        var answer = await Make(chat, 0.5, Hit("a.md", 0, "Cats sleep a lot.", 0.6, 1))
            .AskAsync("What do dogs eat?", TestContext.Current.CancellationToken);

        Assert.True(answer.Refused);
        Assert.Equal(Answerer.NotInContext, answer.RefusalReason);
        Assert.Empty(answer.Citations);
        Assert.Single(answer.Retrieved);
    }

    [Fact]
    public async Task Strips_reasoning_blocks_from_the_answer()
    {
        var chat = new ScriptedChatClient("<think>\nThe passage says cats sleep.\n</think>\n\nCats sleep a lot [1].");
        var answer = await Make(chat, 0.5, Hit("a.md", 0, "Cats sleep a lot.", 0.6, 1))
            .AskAsync("Do cats sleep?", TestContext.Current.CancellationToken);

        Assert.Equal("Cats sleep a lot [1].", answer.Text);
        Assert.Single(answer.Citations);
    }
}
