using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using RagEval.Core.Chunking;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Ingestion.Loaders;

namespace RagEval.Api;

internal static class ServiceSetup
{
    /// <summary>Registers the pipeline: loaders, chunker, embedder, Qdrant store and indexer.</summary>
    public static IServiceCollection AddRagPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.Section));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.Section));
        services.Configure<ChunkingOptions>(configuration.GetSection(ChunkingOptions.Section));

        services.AddSingleton<IDocumentLoader, MarkdownLoader>();
        services.AddSingleton<IDocumentLoader, HtmlLoader>();
        services.AddSingleton(sp => Clients.Qdrant(sp.GetRequiredService<IOptions<QdrantOptions>>().Value));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            Clients.Embeddings(sp.GetRequiredService<IOptions<OllamaOptions>>().Value));
        services.AddSingleton(sp => new Embedder(sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            EmbeddingProfile.For(sp.GetRequiredService<IOptions<OllamaOptions>>().Value.EmbeddingModel)));
        services.AddSingleton<IChunker>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ChunkingOptions>>().Value;
            return new FixedSizeChunker(o.Size, o.Overlap);
        });
        services.AddSingleton(sp => new ChunkStore(sp.GetRequiredService<QdrantClient>(),
            sp.GetRequiredService<IOptions<QdrantOptions>>().Value.Collection));
        services.AddSingleton<Indexer>();
        return services;
    }
}
