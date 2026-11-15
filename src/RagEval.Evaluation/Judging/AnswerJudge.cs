using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace RagEval.Evaluation.Judging;

/// <summary>A judge's three-way verdict.</summary>
public enum Verdict
{
    No = 0,
    Partial = 1,
    Yes = 2,
}

/// <summary>A verdict with the judge's short reason.</summary>
public sealed record JudgeVerdict(Verdict Verdict, string Reason)
{
    /// <summary>1 for yes, 0.5 for partial, 0 for no.</summary>
    public double Score => (int)Verdict / 2.0;
}

/// <summary>
/// A local model as judge of generated answers, with two questions asked separately:
/// <b>faithfulness</b> (is every statement supported by the passages the answer was written from?)
/// and <b>correctness</b> (does it answer the question, judged against a reference?). The model
/// writes a short reason before its verdict, in JSON constrained by a schema, at temperature 0.
/// </summary>
public sealed class AnswerJudge(IChatClient chat, ChatOptions? options = null)
{
    private const string System = """
        You evaluate answers written by a question-answering system. You are strict and literal:
        you judge only what is written, not what the writer may have meant, and you do not use
        outside knowledge. Answers and passages may be in any language. Citation markers such as
        [1] or [2][3] are not statements; ignore them.
        Reply with JSON only: a short reason (one or two sentences, in English), then the verdict.
        """;

    private const string FaithfulnessTask = """
        Is every factual statement in the answer supported by the passages?
        - "yes": every statement is stated in, or directly follows from, the passages.
        - "partial": the main statement is supported, but some detail is not in the passages or contradicts them.
        - "no": the main statement is not supported by the passages, or contradicts them.
        """;

    private const string CorrectnessTask = """
        Does the answer correctly answer the question, according to the reference passage?
        The reference is an excerpt of the source that contains the correct answer; it may hold
        more than the question asks for.
        - "yes": the answer gives what the question asks, consistent with the reference. Other wording, another language, or extra correct details are fine.
        - "partial": the answer is incomplete or only partly correct.
        - "no": the answer is wrong, contradicts the reference, or does not answer the question.
        """;

    private static readonly ChatResponseFormat Format = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "reason": { "type": "string" },
            "verdict": { "type": "string", "enum": ["yes", "partial", "no"] }
          },
          "required": ["reason", "verdict"]
        }
        """).RootElement, "verdict", "A short reason, then the verdict.");

    public async Task<JudgeVerdict> FaithfulnessAsync(string question, string answer, IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passages);
        var prompt = new StringBuilder("Passages:\n\n");
        for (var i = 0; i < passages.Count; i++)
        {
            prompt.Append(CultureInfo.InvariantCulture, $"[{i + 1}] {passages[i]}\n\n");
        }

        prompt.Append(CultureInfo.InvariantCulture, $"Question: {question}\n\nAnswer: {answer}\n\n{FaithfulnessTask}");
        return await AskAsync(prompt.ToString(), cancellationToken);
    }

    public Task<JudgeVerdict> CorrectnessAsync(string question, string answer, string reference, CancellationToken cancellationToken = default) =>
        AskAsync($"Reference passage: {reference}\n\nQuestion: {question}\n\nAnswer: {answer}\n\n{CorrectnessTask}", cancellationToken);

    private async Task<JudgeVerdict> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        var chatOptions = (options ?? new ChatOptions()).Clone();
        chatOptions.ResponseFormat = Format;
        var response = await chat.GetResponseAsync([new(ChatRole.System, System), new(ChatRole.User, prompt)], chatOptions, cancellationToken);
        return Parse(response.Text);
    }

    /// <summary>Reads the judge's JSON; tolerates text around it.</summary>
    public static JudgeVerdict Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new FormatException($"The judge did not reply with JSON: {Shorten(text)}");
        }

        using var json = JsonDocument.Parse(text[start..(end + 1)]);
        var root = json.RootElement;
        var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString()?.Trim().ToLowerInvariant() : null;
        var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
        return verdict switch
        {
            "yes" => new JudgeVerdict(Verdict.Yes, reason),
            "partial" => new JudgeVerdict(Verdict.Partial, reason),
            "no" => new JudgeVerdict(Verdict.No, reason),
            _ => throw new FormatException($"Unknown verdict in: {Shorten(text)}"),
        };
    }

    private static string Shorten(string text) => text.Length > 200 ? text[..200] + "…" : text;
}
