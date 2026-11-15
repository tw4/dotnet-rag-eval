using System.CommandLine;
using System.Diagnostics;
using RagEval.Core.Chunking;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Ingestion;

namespace RagEval.Cli;

/// <summary><c>rag-eval ingest &lt;folder&gt;</c>: load, chunk, embed and index a folder of documents.</summary>
internal static class IngestCommand
{
    public static Command Create()
    {
        var folder = new Argument<DirectoryInfo>("folder") { Description = "Folder with Markdown and HTML files (searched recursively)" };
        var chunkSize = new Option<int>("--chunk-size") { Description = "Largest chunk, in characters", DefaultValueFactory = _ => 1000 };
        var chunkOverlap = new Option<int>("--chunk-overlap") { Description = "Characters shared by consecutive chunks", DefaultValueFactory = _ => 200 };
        var recreate = new Option<bool>("--recreate") { Description = "Delete the collection first" };

        var command = new Command("ingest", "Load, chunk, embed and index every supported document in a folder.")
        {
            folder, chunkSize, chunkOverlap, recreate,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            var dir = result.GetValue(folder)!;
            if (!dir.Exists)
            {
                await result.InvocationConfiguration.Error.WriteLineAsync($"Folder not found: {dir.FullName}");
                return 1;
            }

            using var qdrant = Clients.Qdrant(settings.Qdrant);
            using var generator = Clients.Embeddings(settings.Ollama);
            var embedder = new Embedder(generator, EmbeddingProfile.For(settings.Ollama.EmbeddingModel));
            var indexer = new Indexer(new FixedSizeChunker(result.GetValue(chunkSize), result.GetValue(chunkOverlap)), embedder,
                new ChunkStore(qdrant, settings.Qdrant.Collection))
            { Recreate = result.GetValue(recreate) };

            var watch = Stopwatch.StartNew();
            int documents = 0, chunks = 0;
            await foreach (var document in DocumentReader.Default().ReadFolderAsync(dir.FullName, cancellationToken))
            {
                var indexed = await indexer.IndexAsync(document, cancellationToken);
                documents++;
                chunks += indexed.Count;
                await output.WriteLineAsync($"  {document.Id}: {indexed.Count} chunks");
            }

            await output.WriteLineAsync(
                $"Indexed {documents} documents as {chunks} chunks into '{settings.Qdrant.Collection}' " +
                $"with {settings.Ollama.EmbeddingModel} in {watch.Elapsed.TotalSeconds:F1} s.");
            return 0;
        });
        return command;
    }
}
