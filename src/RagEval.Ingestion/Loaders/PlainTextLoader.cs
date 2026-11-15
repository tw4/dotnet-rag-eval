using RagEval.Core.Documents;

namespace RagEval.Ingestion.Loaders;

/// <summary>
/// Plain text, kept as is. Lines that look like MediaWiki section markers (<c>== Title ==</c>, as
/// in Wikipedia plain-text extracts) become headings; their markers are dropped from the text.
/// </summary>
public sealed class PlainTextLoader : IDocumentLoader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".txt"];

    public Document Load(string id, string source, string content) => Load(id, source, content, title: null);

    public static Document Load(string id, string source, string content, string? title)
    {
        ArgumentNullException.ThrowIfNull(content);
        var builder = new TextBuilder();
        var lineStart = 0;
        while (lineStart <= content.Length)
        {
            var newline = content.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? content.Length : newline;
            var line = content.AsSpan(lineStart, lineEnd - lineStart);
            if (TryParseHeading(line, out var level, out var headingStart, out var headingLength))
            {
                var heading = line.Slice(headingStart, headingLength);
                builder.BeginHeading(level, heading.ToString());
                builder.Append(heading, lineStart + headingStart);
                builder.BreakBlock();
            }
            else if (line.Trim().Length == 0)
            {
                builder.BreakBlock();   // a blank line separates paragraphs
            }
            else
            {
                builder.Append(line, lineStart);
                builder.AppendChar('\n', lineEnd);   // a single newline is just whitespace inside a paragraph
            }

            if (newline < 0)
            {
                break;
            }

            lineStart = newline + 1;
        }

        return builder.Build(id, source, DocumentFormat.Text, title, content);
    }

    /// <summary><c>== Title ==</c> (2–6 equals signs on both sides) → level 1–5.</summary>
    internal static bool TryParseHeading(ReadOnlySpan<char> line, out int level, out int start, out int length)
    {
        level = start = length = 0;
        var trimmed = line.TrimEnd();
        var left = 0;
        while (left < trimmed.Length && trimmed[left] == '=')
        {
            left++;
        }

        var right = 0;
        while (right < trimmed.Length - left && trimmed[trimmed.Length - 1 - right] == '=')
        {
            right++;
        }

        if (left < 2 || left > 6 || left != right)
        {
            return false;
        }

        var inner = trimmed[left..^right];
        var innerTrimmed = inner.Trim();
        if (innerTrimmed.Length == 0)
        {
            return false;
        }

        level = left - 1;
        start = left + (inner.Length - inner.TrimStart().Length);
        length = innerTrimmed.Length;
        return true;
    }
}
