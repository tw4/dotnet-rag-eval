using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;

namespace RagEval.Core.Configuration;

/// <summary>Creates the Qdrant and Ollama clients from configuration.</summary>
public static class Clients
{
    public static QdrantClient Qdrant(QdrantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new QdrantClient(options.GrpcUrl, options.ApiKey);
    }

    /// <summary>An embedding generator for <paramref name="model"/> (default: the configured one).</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> Embeddings(OllamaOptions options, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new OllamaApiClient(options.Url, model ?? options.EmbeddingModel);
    }

    /// <summary>
    /// Deterministic generation for reproducible answers: temperature 0, a fixed seed, and no
    /// separate "thinking" phase for reasoning models (qwen3, deepseek-r1).
    /// </summary>
    public static ChatOptions DeterministicChatOptions(int seed = 42)
    {
        var options = new ChatOptions { Temperature = 0, Seed = seed };
        options.AddOllamaOption(OllamaOption.Think, false);
        return options;
    }

    /// <summary>A chat client for <paramref name="model"/> (default: the configured one).</summary>
    public static IChatClient Chat(OllamaOptions options, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new OllamaApiClient(options.Url, model ?? options.ChatModel);
    }
}
