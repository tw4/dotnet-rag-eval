using RagEval.Core.Documents;

namespace RagEval.Ingestion.Loaders;

/// <summary>Turns a file's content into a <see cref="Document"/> with a source map.</summary>
public interface IDocumentLoader
{
    /// <summary>File extensions this loader handles, lower case with the dot (".md").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    Document Load(string id, string source, string content);
}
