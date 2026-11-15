using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RagEval.Tests.Support;

/// <summary>The API against the test Qdrant, with the hashing embedder instead of Ollama.</summary>
internal sealed class ApiFactory(string collection) : WebApplicationFactory<Program>
{
    public HashingEmbeddingGenerator Embeddings { get; } = new();

    public ScriptedChatClient Chat { get; init; } = new("Install it with docker compose up [1].");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Qdrant:GrpcUrl", QdrantFixture.GrpcUrl.ToString());
        builder.UseSetting("Qdrant:Collection", collection);
        builder.UseSetting("Ollama:EmbeddingModel", "test-embedder");
        builder.UseSetting("Answering:MinScore", "0.3");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Embeddings);
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(Chat);
        });
    }
}
