using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using RagEval.Core.Documents;
using Document = RagEval.Core.Documents.Document;

namespace RagEval.Ingestion.Loaders;

/// <summary>
/// Extracts the visible text of an HTML page with AngleSharp, keeping each text node's offset
/// in the file. Scripts, styles and navigation chrome that is not text are skipped.
/// </summary>
public sealed class HtmlLoader : IDocumentLoader
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "head", "svg", "canvas", "iframe", "object",
    };

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "br", "dd", "details", "dialog", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header",
        "hr", "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "td", "th", "tr", "ul",
    };

    public IReadOnlyCollection<string> Extensions { get; } = [".html", ".htm"];

    public Document Load(string id, string source, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var parser = new HtmlParser(new HtmlParserOptions { IsKeepingSourceReferences = true });
        using var html = parser.ParseDocument(content);
        var state = new WalkState(content);
        Walk(html.Body ?? (INode?)html.DocumentElement, state);
        var title = string.IsNullOrWhiteSpace(html.Title) ? state.FirstH1 : html.Title.Trim();
        return state.Builder.Build(id, source, DocumentFormat.Html, title, content);
    }

    /// <summary>
    /// AngleSharp records source positions for elements only, so text nodes are located by
    /// searching forward from a cursor that follows the document order.
    /// </summary>
    private sealed class WalkState(string content)
    {
        public string Content { get; } = content;
        public TextBuilder Builder { get; } = new();
        public int Cursor { get; set; }
        public string? FirstH1 { get; set; }
    }

    private static void Walk(INode? node, WalkState state)
    {
        if (node is null)
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    AppendText(text.Data, state);
                    break;
                case IElement element:
                    // The recorded position is just inside the tag; start from its '<'.
                    var at = element.SourceReference?.Position.Position ?? -1;
                    if (at >= 0)
                    {
                        at = state.Content.LastIndexOf('<', Math.Min(at, state.Content.Length - 1));
                    }

                    if (at > state.Cursor)
                    {
                        state.Cursor = at;
                    }

                    if (Skipped.Contains(element.LocalName) || element.HasAttribute("hidden"))
                    {
                        SkipPast(element.LocalName, state);
                        break;
                    }

                    VisitElement(element, state);
                    break;
            }
        }
    }

    private static void VisitElement(IElement element, WalkState state)
    {
        var name = element.LocalName;
        var block = Blocks.Contains(name);
        if (name.Length == 2 && name[0] == 'h' && char.IsAsciiDigit(name[1]))
        {
            var heading = element.TextContent.Trim();
            if (state.FirstH1 is null && name[1] == '1')
            {
                state.FirstH1 = heading;
            }

            state.Builder.BeginHeading(name[1] - '0', heading);
        }
        else if (block)
        {
            state.Builder.BreakBlock();
        }

        Walk(element, state);
        if (block)
        {
            state.Builder.BreakBlock();
        }
    }

    private static void AppendText(string value, WalkState state)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            if (value.Length > 0)
            {
                state.Builder.AppendChar(' ', state.Cursor);
            }

            return;
        }

        if (char.IsWhiteSpace(value[0]))
        {
            state.Builder.AppendChar(' ', state.Cursor);
        }

        var content = state.Content;
        // Search from the first text character, never inside a tag (attribute values
        // often contain the same characters as short text nodes, e.g. quotes).
        var from = TextStart(content, state.Cursor);
        var exact = from < content.Length ? content.IndexOf(trimmed, from, StringComparison.Ordinal) : -1;
        var nextTag = from < content.Length ? content.IndexOf('<', from) : -1;
        if (exact >= 0 && (nextTag < 0 || exact < nextTag))
        {
            state.Builder.Append(trimmed, exact);
            state.Cursor = exact + trimmed.Length;
        }
        else
        {
            // Entities make the source longer than the text: align the two.
            var end = nextTag < 0 ? content.Length : nextTag;
            state.Builder.AppendAligned(trimmed, content, from, end);
            state.Cursor = end;
        }

        if (char.IsWhiteSpace(value[^1]))
        {
            state.Builder.AppendChar(' ', state.Cursor);
        }
    }

    /// <summary>First character of text content at or after <paramref name="from"/>, skipping tags.</summary>
    private static int TextStart(string content, int from)
    {
        var i = from;
        while (i < content.Length)
        {
            if (content[i] == '<')
            {
                var close = content.IndexOf('>', i);
                if (close < 0)
                {
                    return content.Length;
                }

                i = close + 1;
            }
            else if (char.IsWhiteSpace(content[i]))
            {
                i++;
            }
            else
            {
                return i;
            }
        }

        return Math.Min(i, content.Length);
    }

    /// <summary>Moves the cursor past the closing tag of a skipped element.</summary>
    private static void SkipPast(string name, WalkState state)
    {
        var close = state.Content.IndexOf("</" + name, state.Cursor, StringComparison.OrdinalIgnoreCase);
        if (close >= 0)
        {
            var end = state.Content.IndexOf('>', close);
            state.Cursor = end < 0 ? state.Content.Length : end + 1;
        }
        else
        {
            var end = state.Content.IndexOf('>', state.Cursor);
            state.Cursor = end < 0 ? state.Content.Length : end + 1;
        }
    }
}
