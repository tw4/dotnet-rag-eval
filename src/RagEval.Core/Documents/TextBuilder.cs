using System.Text;

namespace RagEval.Core.Documents;

/// <summary>
/// Builds extracted text together with its source map. Loaders append text with the source
/// offset of each character; whitespace is collapsed and blocks are separated by a blank line.
/// </summary>
public sealed class TextBuilder
{
    private readonly StringBuilder _text = new();
    private readonly List<int> _offsets = [];
    private readonly List<Heading> _headings = [];
    private readonly List<int> _pageStarts = [];
    private bool _pendingSpace;
    private bool _pendingBreak;

    public int Length => _text.Length;

    public IReadOnlyList<Heading> Headings => _headings;

    /// <summary>
    /// Appends <paramref name="value"/>, whose first character is at <paramref name="sourceOffset"/>
    /// in the source and whose characters are contiguous there.
    /// </summary>
    public void Append(ReadOnlySpan<char> value, int sourceOffset)
    {
        for (var i = 0; i < value.Length; i++)
        {
            AppendChar(value[i], sourceOffset + i);
        }
    }

    /// <summary>
    /// Appends <paramref name="value"/>, which was decoded from <c>source[runStart..runEnd)</c>
    /// (entities, escapes, padding). Characters are aligned with the source: equal characters map
    /// exactly, an entity such as <c>&amp;amp;</c> maps its decoded character to the <c>&amp;</c>, and
    /// runs of whitespace match each other.
    /// </summary>
    public void AppendAligned(ReadOnlySpan<char> value, string source, int runStart, int runEnd)
    {
        ArgumentNullException.ThrowIfNull(source);
        runEnd = Math.Min(runEnd, source.Length);
        var j = Math.Max(0, runStart);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                AppendChar(c, Math.Min(j, Math.Max(0, runEnd - 1)));
                while (j < runEnd && char.IsWhiteSpace(source[j]))
                {
                    j++;
                }

                continue;
            }

            // Skip source characters that produced no text (markup, padding, backslashes).
            var k = j;
            while (k < runEnd && source[k] != c && source[k] != '&')
            {
                k++;
            }

            if (k < runEnd && source[k] == c)
            {
                AppendChar(c, k);
                j = k + 1;
            }
            else if (k < runEnd && source[k] == '&')
            {
                var semicolon = source.IndexOf(';', k, Math.Min(12, runEnd - k));
                AppendChar(c, k);
                j = semicolon < 0 ? k + 1 : semicolon + 1;
            }
            else
            {
                AppendChar(c, Math.Min(j, Math.Max(0, runEnd - 1)));
            }
        }
    }

    /// <summary>Appends one character; runs of whitespace become a single space.</summary>
    public void AppendChar(char c, int sourceOffset)
    {
        if (char.IsWhiteSpace(c))
        {
            if (_text.Length > 0 && !_pendingBreak)
            {
                _pendingSpace = true;
            }

            return;
        }

        if (_pendingBreak && _text.Length > 0)
        {
            Emit('\n', sourceOffset);
            Emit('\n', sourceOffset);
        }
        else if (_pendingSpace)
        {
            Emit(' ', sourceOffset);
        }

        _pendingBreak = false;
        _pendingSpace = false;
        Emit(c, sourceOffset);
    }

    /// <summary>Ends the current block: the next text starts after a blank line.</summary>
    public void BreakBlock()
    {
        if (_text.Length > 0)
        {
            _pendingBreak = true;
            _pendingSpace = false;
        }
    }

    /// <summary>Starts a heading block; call before appending the heading's text.</summary>
    public void BeginHeading(int level, string title)
    {
        BreakBlock();
        // The heading starts where its first character will land.
        var start = _text.Length + (_text.Length > 0 ? 2 : 0);
        _headings.Add(new Heading(level, title.Trim(), start));
    }

    /// <summary>Marks the start of a new page (paged formats).</summary>
    public void BeginPage()
    {
        BreakBlock();
        _pageStarts.Add(_text.Length + (_text.Length > 0 ? 2 : 0));
    }

    public Document Build(string id, string source, DocumentFormat format, string? title, string sourceText) =>
        new(id, source, format, title, _text.ToString(),
            new SourceMap([.. _offsets], sourceText, _pageStarts.Count > 0 ? [.. _pageStarts] : null), _headings);

    private void Emit(char c, int sourceOffset)
    {
        _text.Append(c);
        _offsets.Add(sourceOffset);
    }
}
