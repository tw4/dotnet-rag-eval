using System.CommandLine;
using RagEval.Core.Chunking;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Evaluation.Datasets;

namespace RagEval.Cli;

/// <summary><c>rag-eval datasets list|download|ingest</c>.</summary>
internal static class DatasetsCommand
{
    public static readonly Option<FileInfo?> ManifestOption = new("--manifest") { Description = "Dataset manifest (default: datasets/datasets.json in this or a parent folder)", Recursive = true };
    public static readonly Option<DirectoryInfo?> DataOption = new("--data-dir") { Description = "Where datasets are downloaded (default: data/ next to the manifest)", Recursive = true };

    public static (DatasetManifest Manifest, string DataDirectory) Resolve(ParseResult result)
    {
        var manifestPath = result.GetValue(ManifestOption)?.FullName ?? DatasetManifest.FindDefault();
        var data = result.GetValue(DataOption)?.FullName
            ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath))!, "data");
        return (DatasetManifest.Load(manifestPath), data);
    }

    /// <summary>Downloads (if needed) and loads a dataset.</summary>
    public static async Task<EvalDataset> LoadAsync(ParseResult result, string id, CancellationToken cancellationToken)
    {
        var (manifest, data) = Resolve(result);
        var info = manifest.Get(id);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await new DatasetDownloader(http, data).EnsureAsync(info, m => result.InvocationConfiguration.Output.WriteLine(m), cancellationToken);
        return EvalDataset.Load(info, data);
    }

    public static Command Create()
    {
        var command = new Command("datasets", "Evaluation datasets: list, download, index.");
        command.Options.Add(ManifestOption);
        command.Options.Add(DataOption);
        command.Subcommands.Add(List());
        command.Subcommands.Add(Download());
        command.Subcommands.Add(Ingest());
        return command;
    }

    private static Command List()
    {
        var command = new Command("list", "List the datasets in the manifest.");
        command.SetAction(result =>
        {
            var (manifest, _) = Resolve(result);
            foreach (var d in manifest.Datasets)
            {
                result.InvocationConfiguration.Output.WriteLine(
                    $"{d.Id,-24} {d.Language}  {d.Size.Documents,6} docs  {d.Size.Queries,4} queries  {d.License}");
            }
        });
        return command;
    }

    private static Command Download()
    {
        var ids = new Argument<string[]>("ids") { Description = "Dataset ids (default: all)", Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("download", "Download datasets into the data folder, verifying checksums.") { ids };
        command.SetAction(async (result, cancellationToken) =>
        {
            var (manifest, data) = Resolve(result);
            var selected = result.GetValue(ids) is { Length: > 0 } list ? list : [.. manifest.Datasets.Select(d => d.Id)];
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var downloader = new DatasetDownloader(http, data);
            foreach (var id in selected)
            {
                var info = manifest.Get(id);
                await downloader.EnsureAsync(info, m => result.InvocationConfiguration.Output.WriteLine(m), cancellationToken);
                var dataset = EvalDataset.Load(info, data);
                await result.InvocationConfiguration.Output.WriteLineAsync(
                    $"{id}: {dataset.Corpus.Count} documents, {dataset.Queries.Count} queries, {dataset.Qrels.Sum(q => q.Value.Count)} judgements (verified)");
            }

            return 0;
        });
        return command;
    }

    private static Command Ingest()
    {
        var id = new Argument<string>("id") { Description = "Dataset id" };
        var chunkSize = new Option<int>("--chunk-size") { Description = "Largest chunk, in characters", DefaultValueFactory = _ => 1000 };
        var chunkOverlap = new Option<int>("--chunk-overlap") { Description = "Characters shared by consecutive chunks", DefaultValueFactory = _ => 200 };
        var force = new Option<bool>("--force") { Description = "Re-index even if a complete index exists" };
        var command = new Command("ingest", "Index a dataset's corpus into its own collection (reused when complete).")
        {
            id, chunkSize, chunkOverlap, force,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            var dataset = await LoadAsync(result, result.GetValue(id)!, cancellationToken);
            using var qdrant = Clients.Qdrant(settings.Qdrant);
            using var embeddings = Clients.Embeddings(settings.Ollama);
            var index = new CorpusIndex(qdrant, new FixedSizeChunker(result.GetValue(chunkSize), result.GetValue(chunkOverlap)),
                new Embedder(embeddings, EmbeddingProfile.For(settings.Ollama.EmbeddingModel)));
            var done = await index.EnsureAsync(dataset, result.GetValue(force), m => output.WriteLine(m), cancellationToken);
            await output.WriteLineAsync(done.Reused
                ? $"{done.Collection}: already indexed ({done.Documents} documents, {done.Chunks} chunks)."
                : $"{done.Collection}: indexed {done.Documents} documents as {done.Chunks} chunks in {done.Elapsed.TotalSeconds:F0} s.");
            return 0;
        });
        return command;
    }
}
