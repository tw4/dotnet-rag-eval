using System.Globalization;
using OllamaSharp;
using OllamaSharp.Models;
using RagEval.Core.Indexing;
using RagEval.Core.Telemetry;

namespace RagEval.Core.Reranking;

/// <summary>Scores how well each passage answers a query, for reordering retrieved chunks.</summary>
public interface IReranker
{
    /// <summary>A short name for reports, e.g. <c>qwen3-reranker:0.6b</c>.</summary>
    string Name { get; }

    /// <summary>One relevance score per passage, higher is more relevant.</summary>
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default);
}

/// <summary>
/// A generative model used as a pointwise reranker, as Qwen3-Reranker is meant to be used: the
/// model is asked whether the passage meets the query and answers "yes" or "no"; the score is
/// P(yes) / (P(yes) + P(no)) of its first token, read from the log-probabilities Ollama returns.
/// Works with the dedicated Qwen3-Reranker models and with any Qwen-format chat model.
/// </summary>
public sealed class YesNoReranker(OllamaApiClient client, string model, int parallelism = 4,
    string instruction = YesNoReranker.DefaultInstruction) : IReranker
{
    /// <summary>The instruction from the Qwen3-Reranker model card.</summary>
    public const string DefaultInstruction = "Given a web search query, retrieve relevant passages that answer the query";

    /// <summary>Longest passage sent to the model, in characters; longer ones are cut.</summary>
    public const int MaxPassageLength = 4000;

    private const string System =
        "Judge whether the Document meets the requirements based on the Query and the Instruct provided. " +
        "Note that the answer can only be \"yes\" or \"no\".";

    public string Name => model;

    /// <summary>The raw prompt: Qwen chat format with an empty thinking block, as in the model card.</summary>
    public static string Prompt(string instruction, string query, string passage)
    {
        ArgumentNullException.ThrowIfNull(passage);
        var document = passage.Length > MaxPassageLength ? passage[..MaxPassageLength] : passage;
        return $"<|im_start|>system\n{System}<|im_end|>\n<|im_start|>user\n<Instruct>: {instruction}\n<Query>: {query}\n" +
               $"<Document>: {document}<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
    }

    /// <summary>P(yes) / (P(yes) + P(no)) from the top log-probabilities of the first generated token.</summary>
    public static double Score(IEnumerable<(string Token, double LogProbability)> top)
    {
        ArgumentNullException.ThrowIfNull(top);
        double? yes = null, no = null;
        foreach (var (token, logprob) in top)
        {
            if (token == "yes")
            {
                yes ??= logprob;
            }
            else if (token == "no")
            {
                no ??= logprob;
            }
        }

        // A token missing from the top list is less likely than all of them: treat it as impossible.
        return (yes, no) switch
        {
            (null, null) => 0.5,
            (null, _) => 0,
            (_, null) => 1,
            ({ } y, { } n) => 1 / (1 + Math.Exp(n - y)),
        };
    }

    public async Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passages);
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var tasks = passages.Select(async passage =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await ScoreOneAsync(query, passage, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<double> ScoreOneAsync(string query, string passage, CancellationToken cancellationToken)
    {
        var request = new GenerateRequest
        {
            Model = model,
            Prompt = Prompt(instruction, query, passage),
            Raw = true,
            Stream = false,
            Logprobs = true,
            TopLogprobs = 20,
            Options = new RequestOptions { NumPredict = 1, Temperature = 0 },
        };
        await foreach (var response in client.GenerateAsync(request, cancellationToken))
        {
            var first = response?.Logprobs?.FirstOrDefault();
            if (first?.TopLogprobs is { } top)
            {
                return Score(top.Where(t => t.LogProbability is not null).Select(t => (t.Token ?? "", t.LogProbability!.Value)));
            }
        }

        throw new InvalidOperationException(
            $"Ollama returned no log-probabilities for '{model}'. Reranking needs Ollama 0.12.11 or later and a generative model.");
    }
}

/// <summary>Reorders the top of a retrieved list by a reranker's scores.</summary>
public static class Rerank
{
    /// <summary>
    /// The first <paramref name="candidates"/> chunks, sorted by <paramref name="scores"/> (ties keep
    /// their retrieval order) and carrying them as their score, followed by the rest in retrieval
    /// order with negative scores, so any ranking by score keeps reranked chunks first.
    /// </summary>
    public static IReadOnlyList<RetrievedChunk> Apply(IReadOnlyList<RetrievedChunk> retrieved, IReadOnlyList<double> scores, int candidates)
    {
        ArgumentNullException.ThrowIfNull(retrieved);
        ArgumentNullException.ThrowIfNull(scores);
        var head = Math.Min(candidates, retrieved.Count);
        if (scores.Count != head)
        {
            throw new ArgumentException($"Expected {head} scores, got {scores.Count}.", nameof(scores));
        }

        var reranked = retrieved.Take(head)
            .Select((chunk, i) => chunk with { Score = scores[i] })
            .OrderByDescending(c => c.Score)
            .ToList();
        reranked.AddRange(retrieved.Skip(head).Select((chunk, i) => chunk with { Score = -(i + 1) }));
        return reranked;
    }

    /// <summary>What the reranker reads for a chunk: its section path, if any, then its text.</summary>
    public static string Passage(RetrievedChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.Chunk.Section is { } section ? string.Create(CultureInfo.InvariantCulture, $"{section}\n{chunk.Chunk.Text}") : chunk.Chunk.Text;
    }
}

/// <summary>A retriever followed by a reranker over its first <paramref name="candidates"/> results.</summary>
public sealed class RerankingRetriever(IRetriever inner, IReranker reranker, int candidates = 20) : IRetriever
{
    public string Name => $"{inner.Name} > rerank({reranker.Name}, {candidates})";

    public bool NeedsQueryVector => inner.NeedsQueryVector;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, ReadOnlyMemory<float> queryVector, int limit,
        CancellationToken cancellationToken = default)
    {
        var retrieved = await inner.RetrieveAsync(query, queryVector, Math.Max(limit, candidates), cancellationToken);
        var head = retrieved.Take(candidates).Select(Rerank.Passage).ToList();
        IReadOnlyList<double> scores;
        using (Stages.Start(Stages.Rerank))
        {
            scores = await reranker.ScoreAsync(query, head, cancellationToken);
        }

        return [.. Rerank.Apply(retrieved, scores, candidates).Take(limit)];
    }
}
