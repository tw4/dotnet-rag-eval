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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Qdrant:GrpcUrl", QdrantFixture.GrpcUrl.ToString());
        builder.UseSetting("Qdrant:Collection", collection);
        builder.UseSetting("Ollama:EmbeddingModel", "test-embedder");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Embeddings);
        });
    }
}
