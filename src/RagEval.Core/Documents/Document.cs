namespace RagEval.Core.Documents;

/// <summary>The format a document was loaded from.</summary>
public enum DocumentFormat
{
    Markdown,
    Html,
    Pdf,
    Text,
}

/// <summary>A heading in a document's extracted text, for structure-aware chunking.</summary>
/// <param name="Level">1 for a top-level heading, up to 6.</param>
/// <param name="Title">The heading text.</param>
/// <param name="TextOffset">Where the heading starts in <see cref="Document.Text"/>.</param>
public sealed record Heading(int Level, string Title, int TextOffset);

/// <summary>
/// A loaded document: its plain text, and a map from every character of that text back to the
/// original file, so a chunk can be cited by its exact position in the source.
/// </summary>
public sealed class Document
{
    public Document(string id, string source, DocumentFormat format, string? title, string text, SourceMap map,
        IReadOnlyList<Heading> headings)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(map);
        if (map.TextLength != text.Length)
        {
            throw new ArgumentException("The source map must cover the text exactly.", nameof(map));
        }

        Id = id;
        Source = source;
        Format = format;
        Title = title;
        Text = text;
        Map = map;
        Headings = headings;
    }

    /// <summary>Stable identifier, e.g. the path relative to the ingested folder.</summary>
    public string Id { get; }

    /// <summary>Where the document came from (file path or URL).</summary>
    public string Source { get; }

    public DocumentFormat Format { get; }

    public string? Title { get; }

    /// <summary>Extracted plain text: markup removed, blocks separated by blank lines.</summary>
    public string Text { get; }

    public SourceMap Map { get; }

    public IReadOnlyList<Heading> Headings { get; }

    /// <summary>The source span of a range of <see cref="Text"/>.</summary>
    public SourceSpan SpanOf(int textStart, int textEnd) => Map.SpanOf(textStart, textEnd);
}
