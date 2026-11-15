using RagEval.Core.Documents;
using RagEval.Ingestion.Loaders;

namespace RagEval.Ingestion;

/// <summary>Loads every supported file under a folder, using the loader for its extension.</summary>
public sealed class DocumentReader
{
    private readonly Dictionary<string, IDocumentLoader> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public DocumentReader(IEnumerable<IDocumentLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        foreach (var loader in loaders)
        {
            foreach (var extension in loader.Extensions)
            {
                _byExtension[extension] = loader;
            }
        }
    }

    /// <summary>A reader with every built-in loader.</summary>
    public static DocumentReader Default() => new([new MarkdownLoader(), new HtmlLoader(), new PlainTextLoader()]);

    public IReadOnlyCollection<string> Extensions => _byExtension.Keys;

    public bool CanRead(string path) => _byExtension.ContainsKey(Path.GetExtension(path));

    /// <summary>Reads one file; its id is the path relative to <paramref name="root"/>, with forward slashes.</summary>
    public async Task<Document> ReadFileAsync(string path, string root, CancellationToken cancellationToken = default)
    {
        var loader = _byExtension.GetValueOrDefault(Path.GetExtension(path))
            ?? throw new NotSupportedException($"No loader for '{Path.GetExtension(path)}' files ({path}).");
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var id = Path.GetRelativePath(root, path).Replace('\\', '/');
        return loader.Load(id, path, content);
    }

    /// <summary>Reads every supported file under <paramref name="folder"/>, in path order.</summary>
    public async IAsyncEnumerable<Document> ReadFolderAsync(string folder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(CanRead)
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            yield return await ReadFileAsync(file, folder, cancellationToken);
        }
    }
}
