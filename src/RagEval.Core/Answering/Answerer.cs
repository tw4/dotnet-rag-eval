using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;

namespace RagEval.Core.Answering;

/// <summary>Settings for answering. Section: <c>Answering</c>.</summary>
public sealed class AnsweringOptions
{
    public const string Section = "Answering";

    /// <summary>Chunks given to the model.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Chunks scoring below this (cosine similarity) are not relevant. If none passes, the question
    /// is refused without calling the model. Depends on the embedding model; see the evaluation.
    /// </summary>
    public double MinScore { get; set; } = 0.5;
}

/// <summary>
/// Answers a question from retrieved chunks: retrieve the top-k, refuse if nothing is relevant,
/// otherwise ask the model to answer only from the numbered context and cite it as [n].
/// </summary>
public sealed partial class Answerer(Embedder embedder, IRetriever retriever, IChatClient chat, AnsweringOptions options,
    ChatOptions? chatOptions = null)
{
    /// <summary>The exact sentence the model is told to use when the context does not answer the question.</summary>
    public const string RefusalSentence = "I could not find the answer in the provided documents.";

    public const string NothingRetrieved = "nothing relevant was retrieved";
    public const string NotInContext = "the retrieved documents do not contain the answer";

    private const string SystemPrompt = $"""
        You answer questions using only the numbered context passages you are given.
        Rules:
        - Use only facts stated in the context. Do not use outside knowledge.
        - After every sentence that uses a passage, cite it with its number in square brackets, e.g. [2]. Cite several as [1][3].
        - If the context does not contain the answer, reply with exactly: {RefusalSentence}
        - Answer in the language of the question. Be concise.
        """;

    public async Task<Answer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var vector = await embedder.EmbedQueryAsync(question, cancellationToken);
        var retrieved = (await retriever.RetrieveAsync(question, vector, options.TopK, cancellationToken))
            .Where(c => c.Score >= options.MinScore)
            .ToList();
        if (retrieved.Count == 0)
        {
            return new Answer(RefusalSentence, true, NothingRetrieved, [], []);
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildPrompt(question, retrieved)),
        };
        var response = await chat.GetResponseAsync(messages, chatOptions, cancellationToken);
        var text = StripThinking(response.Text).Trim();

        if (text.Contains(RefusalSentence, StringComparison.OrdinalIgnoreCase) || text.Length == 0)
        {
            return new Answer(RefusalSentence, true, NotInContext, [], retrieved);
        }

        return new Answer(text, false, null, ExtractCitations(text, retrieved), retrieved);
    }

    internal static string BuildPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        var sb = new StringBuilder("Context:\n\n");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var heading = string.Join(" — ", new[] { c.Title, c.Chunk.Section }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
            sb.Append(CultureInfo.InvariantCulture, $"[{i + 1}] ({c.Chunk.DocumentId}{(heading.Length > 0 ? ": " + heading : "")})\n");
            sb.Append(c.Chunk.Text).Append("\n\n");
        }

        sb.Append("Question: ").Append(question);
        return sb.ToString();
    }

    /// <summary>Citations for every valid [n] in the answer, in order of first appearance.</summary>
    internal static IReadOnlyList<Citation> ExtractCitations(string text, IReadOnlyList<RetrievedChunk> chunks)
    {
        var seen = new HashSet<int>();
        var citations = new List<Citation>();
        foreach (Match m in CitationPattern().Matches(text))
        {
            foreach (var part in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, CultureInfo.InvariantCulture, out var n) || n < 1 || n > chunks.Count || !seen.Add(n))
                {
                    continue;
                }

                var c = chunks[n - 1];
                var s = c.Chunk.Source;
                citations.Add(new Citation(n, c.Chunk.DocumentId, c.Chunk.Id, c.Title, c.Chunk.Section, c.Source,
                    s.StartLine, s.EndLine, s.Start, s.End, s.Page, c.Score, c.Chunk.Text));
            }
        }

        return citations;
    }

    /// <summary>Removes &lt;think&gt; blocks some reasoning models emit before the answer.</summary>
    internal static string StripThinking(string text) => ThinkPattern().Replace(text, "");

    [GeneratedRegex(@"\[(\d+(?:\s*,\s*\d+)*)\]")]
    private static partial Regex CitationPattern();

    [GeneratedRegex(@"<think>.*?(</think>|$)", RegexOptions.Singleline)]
    private static partial Regex ThinkPattern();
}
