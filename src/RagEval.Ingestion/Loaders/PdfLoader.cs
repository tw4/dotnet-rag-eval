using System.Text;
using RagEval.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Outline;

namespace RagEval.Ingestion.Loaders;

/// <summary>
/// PDF files with a text layer. Each page's text is extracted in reading order; the "source" of
/// the source map is the extracted text of all pages joined by form feeds (as <c>pdftotext</c>
/// writes it), so every chunk knows its page, and its lines refer to that text. Bookmarks
/// (the outline) become headings where their title starts a line on their page. Words
/// hyphenated across a line break are joined. Scanned PDFs without a text layer give an empty
/// document: OCR is out of scope.
/// </summary>
public sealed class PdfLoader : IDocumentLoader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public Document Load(string id, string source, string content) =>
        throw new NotSupportedException("PDF is a binary format: load it from its bytes.");

    public Document Load(string id, string source, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var pdf = PdfDocument.Open(content);
        var pages = pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p, addDoubleNewline: true).Replace("\r\n", "\n", StringComparison.Ordinal)).ToList();
        var bookmarks = Bookmarks(pdf);

        var sourceText = new StringBuilder();
        var builder = new TextBuilder();
        for (var p = 0; p < pages.Count; p++)
        {
            if (p > 0)
            {
                sourceText.Append('\f');
            }

            var pageOffset = sourceText.Length;
            sourceText.Append(pages[p]);
            builder.BeginPage();
            AppendPage(builder, pages[p], pageOffset, bookmarks.Where(b => b.Page == p + 1).ToList());
        }

        var title = string.IsNullOrWhiteSpace(pdf.Information.Title) ? bookmarks.FirstOrDefault().Title : pdf.Information.Title.Trim();
        return builder.Build(id, source, DocumentFormat.Pdf, title, sourceText.ToString());
    }

    private static List<(int Page, int Level, string Title)> Bookmarks(PdfDocument pdf)
    {
        var result = new List<(int, int, string)>();
        if (!pdf.TryGetBookmarks(out var bookmarks))
        {
            return result;
        }

        void Walk(IEnumerable<BookmarkNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is DocumentBookmarkNode { PageNumber: > 0 } doc && !string.IsNullOrWhiteSpace(node.Title))
                {
                    result.Add((doc.PageNumber, Math.Clamp(node.Level + 1, 1, 6), node.Title.Trim()));
                }

                Walk(node.Children);
            }
        }

        Walk(bookmarks.Roots);
        return result;
    }

    private static string Squash(ReadOnlySpan<char> text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendPage(TextBuilder builder, string page, int pageOffset, List<(int Page, int Level, string Title)> headings)
    {
        var lines = new List<(int Start, int End)>();
        for (var start = 0; start <= page.Length;)
        {
            var newline = page.IndexOf('\n', start);
            var end = newline < 0 ? page.Length : newline;
            lines.Add((start, end));
            start = end + 1;
        }

        var joinWithPrevious = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var (start, end) = lines[i];
            var line = page.AsSpan(start, end - start);
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                builder.BreakBlock();
                joinWithPrevious = false;
                continue;
            }

            var leading = line.Length - line.TrimStart().Length;
            var key = Squash(trimmed);
            var heading = headings.FindIndex(h => key == Squash(h.Title));
            if (heading >= 0)
            {
                builder.BeginHeading(headings[heading].Level, headings[heading].Title);
                builder.Append(trimmed, pageOffset + start + leading);
                builder.BreakBlock();
                headings.RemoveAt(heading);
                joinWithPrevious = false;
                continue;
            }

            if (!joinWithPrevious)
            {
                builder.AppendChar(' ', pageOffset + start);   // a line break inside a paragraph is a space
            }

            // "exam-" at the end of a line followed by "ple": drop the hyphen and the break.
            var next = i + 1 < lines.Count ? page.AsSpan(lines[i + 1].Start, lines[i + 1].End - lines[i + 1].Start).TrimStart() : [];
            joinWithPrevious = trimmed.Length >= 2 && trimmed[^1] == '-' && char.IsLetter(trimmed[^2]) &&
                               next.Length > 0 && char.IsLower(next[0]);
            var text = joinWithPrevious ? trimmed[..^1] : trimmed;
            builder.Append(text, pageOffset + start + leading);
        }
    }
}
