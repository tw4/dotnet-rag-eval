namespace RagEval.Core.Configuration;

/// <summary>Where the Qdrant vector database runs. Section: <c>Qdrant</c>.</summary>
public sealed class QdrantOptions
{
    public const string Section = "Qdrant";

    /// <summary>REST endpoint, e.g. <c>http://localhost:6333</c>.</summary>
    public Uri Url { get; set; } = new("http://localhost:6333");

    /// <summary>gRPC endpoint used by the .NET client, e.g. <c>http://localhost:6334</c>.</summary>
    public Uri GrpcUrl { get; set; } = new("http://localhost:6334");

    /// <summary>Optional API key (Qdrant <c>service.api_key</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Collection that holds the indexed chunks.</summary>
    public string Collection { get; set; } = "documents";
}

/// <summary>Where Ollama runs and which models it serves. Section: <c>Ollama</c>.</summary>
public sealed class OllamaOptions
{
    public const string Section = "Ollama";

    public Uri Url { get; set; } = new("http://localhost:11434");

    /// <summary>Model used to embed chunks and questions.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Model used to write answers.</summary>
    public string ChatModel { get; set; } = "qwen3:4b";
}
