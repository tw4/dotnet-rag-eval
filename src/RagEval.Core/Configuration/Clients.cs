using Microsoft.Extensions.AI;
using OllamaSharp;
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

    /// <summary>A chat client for <paramref name="model"/> (default: the configured one).</summary>
    public static IChatClient Chat(OllamaOptions options, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new OllamaApiClient(options.Url, model ?? options.ChatModel);
    }
}
