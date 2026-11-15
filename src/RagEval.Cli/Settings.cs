using System.CommandLine;
using Microsoft.Extensions.Configuration;
using RagEval.Core.Configuration;

namespace RagEval.Cli;

/// <summary>
/// Connection settings shared by commands. Precedence: command-line options, then environment
/// variables (<c>Qdrant__GrpcUrl</c>, <c>Ollama__Url</c>, … as for the API), then <c>rag-eval.json</c>.
/// </summary>
internal sealed class Settings
{
    public static readonly Option<Uri?> QdrantOption = new("--qdrant") { Description = "Qdrant gRPC URL (default http://localhost:6334)", Recursive = true };
    public static readonly Option<Uri?> OllamaOption = new("--ollama") { Description = "Ollama URL (default http://localhost:11434)", Recursive = true };
    public static readonly Option<string?> CollectionOption = new("--collection") { Description = "Qdrant collection (default documents)", Recursive = true };
    public static readonly Option<string?> EmbeddingModelOption = new("--embedding-model") { Description = "Ollama embedding model (default nomic-embed-text)", Recursive = true };

    public required QdrantOptions Qdrant { get; init; }

    public required OllamaOptions Ollama { get; init; }

    public static void AddGlobalOptions(RootCommand root)
    {
        root.Options.Add(QdrantOption);
        root.Options.Add(OllamaOption);
        root.Options.Add(CollectionOption);
        root.Options.Add(EmbeddingModelOption);
    }

    public static Settings From(ParseResult result)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "rag-eval.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
        var qdrant = configuration.GetSection(QdrantOptions.Section).Get<QdrantOptions>() ?? new QdrantOptions();
        var ollama = configuration.GetSection(OllamaOptions.Section).Get<OllamaOptions>() ?? new OllamaOptions();

        if (result.GetValue(QdrantOption) is { } q)
        {
            qdrant.GrpcUrl = q;
        }

        if (result.GetValue(OllamaOption) is { } o)
        {
            ollama.Url = o;
        }

        if (result.GetValue(CollectionOption) is { } c)
        {
            qdrant.Collection = c;
        }

        if (result.GetValue(EmbeddingModelOption) is { } m)
        {
            ollama.EmbeddingModel = m;
        }

        return new Settings { Qdrant = qdrant, Ollama = ollama };
    }
}
