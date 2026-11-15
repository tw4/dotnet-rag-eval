using System.Text;
using RagEval.Core.Documents;

namespace RagEval.Ingestion.Loaders;

/// <summary>Turns a file's content into a <see cref="Document"/> with a source map.</summary>
public interface IDocumentLoader
{
    /// <summary>File extensions this loader handles, lower case with the dot (".md").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>Loads a text format from its decoded content.</summary>
    Document Load(string id, string source, string content);

    /// <summary>
    /// Loads a file from its bytes. Text formats decode them (UTF-8 unless a byte order mark says
    /// otherwise); binary formats such as PDF override this.
    /// </summary>
    Document Load(string id, string source, byte[] content) => Load(id, source, Decode(content));

    static string Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
