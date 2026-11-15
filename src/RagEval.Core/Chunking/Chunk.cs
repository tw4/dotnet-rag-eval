using RagEval.Core.Documents;

namespace RagEval.Core.Chunking;

/// <summary>A piece of a document that is embedded and retrieved on its own.</summary>
/// <param name="DocumentId">The document the chunk comes from.</param>
/// <param name="Index">Position of the chunk within its document, from 0.</param>
/// <param name="Text">The chunk's text, as embedded.</param>
/// <param name="TextStart">Start of the chunk in <see cref="Document.Text"/>.</param>
/// <param name="TextEnd">End of the chunk in <see cref="Document.Text"/> (exclusive).</param>
/// <param name="Source">Where the chunk is in the original file, for citations.</param>
/// <param name="Section">Heading path of the section the chunk starts in, if known (e.g. "Install › Docker").</param>
/// <param name="EmbeddingText">
/// What is embedded when it differs from <paramref name="Text"/>: sentence-window chunks embed one
/// sentence but return the window around it.
/// </param>
public sealed record Chunk(string DocumentId, int Index, string Text, int TextStart, int TextEnd, SourceSpan Source,
    string? Section = null, string? EmbeddingText = null)
{
    /// <summary>The text that is embedded for this chunk.</summary>
    public string TextToEmbed => EmbeddingText ?? Text;

    /// <summary>Stable id: the document id and the chunk index.</summary>
    public string Id => $"{DocumentId}#{Index}";
}

/// <summary>Splits documents into chunks.</summary>
public interface IChunker
{
    /// <summary>A short name for reports, e.g. <c>fixed-1000-200</c>.</summary>
    string Name { get; }

    IReadOnlyList<Chunk> Split(Document document);
}
