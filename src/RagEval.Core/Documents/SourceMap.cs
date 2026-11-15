namespace RagEval.Core.Documents;

/// <summary>A position range in an original file, for citations.</summary>
/// <param name="Start">First character, as an offset into the original file (inclusive).</param>
/// <param name="End">Offset just past the last character (exclusive).</param>
/// <param name="StartLine">1-based line of <paramref name="Start"/>.</param>
/// <param name="EndLine">1-based line of the last character.</param>
/// <param name="Page">1-based page for paged formats (PDF), otherwise null.</param>
public sealed record SourceSpan(int Start, int End, int StartLine, int EndLine, int? Page = null);

/// <summary>
/// Maps each character of extracted text to its offset in the original file. Characters the
/// extractor inserted (block separators) map to the nearest preceding source character.
/// </summary>
public sealed class SourceMap
{
    private readonly int[] _offsets;      // per text char: offset in the source
    private readonly int[] _lineStarts;   // offsets where each source line starts
    private readonly int[]? _pageStarts;  // text offsets where each page starts (PDF)

    public SourceMap(int[] offsets, string source, int[]? pageStarts = null)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(source);
        _offsets = offsets;
        _pageStarts = pageStarts;
        var lines = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                lines.Add(i + 1);
            }
        }

        _lineStarts = [.. lines];
    }

    public int TextLength => _offsets.Length;

    /// <summary>Source offset of the text character at <paramref name="textOffset"/>.</summary>
    public int SourceOffset(int textOffset) => _offsets[textOffset];

    /// <summary>1-based source line of a source offset.</summary>
    public int LineOf(int sourceOffset)
    {
        var i = Array.BinarySearch(_lineStarts, sourceOffset);
        return (i >= 0 ? i : ~i - 1) + 1;
    }

    /// <summary>1-based page of a text offset, or null for unpaged documents.</summary>
    public int? PageOf(int textOffset)
    {
        if (_pageStarts is null)
        {
            return null;
        }

        var i = Array.BinarySearch(_pageStarts, textOffset);
        return (i >= 0 ? i : ~i - 1) + 1;
    }

    /// <summary>The source span covering text characters <c>[textStart, textEnd)</c>.</summary>
    public SourceSpan SpanOf(int textStart, int textEnd)
    {
        if (textStart < 0 || textEnd > _offsets.Length || textStart >= textEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(textStart), $"Invalid text range [{textStart}, {textEnd}).");
        }

        // Offsets are monotonic, but separators repeat the previous offset, so take min/max.
        var start = int.MaxValue;
        var last = int.MinValue;
        for (var i = textStart; i < textEnd; i++)
        {
            start = Math.Min(start, _offsets[i]);
            last = Math.Max(last, _offsets[i]);
        }

        return new SourceSpan(start, last + 1, LineOf(start), LineOf(last), PageOf(textStart));
    }
}
