using RagEval.Core.Documents;

namespace RagEval.Core.Chunking;

/// <summary>
/// Splits text into windows of at most <see cref="Size"/> characters, each starting
/// <see cref="Size"/> − <see cref="Overlap"/> characters after the previous one. Boundaries move to
/// the nearest whitespace so words are never cut; a word longer than a whole window is the only
/// exception.
/// </summary>
public sealed class FixedSizeChunker : IChunker
{
    public FixedSizeChunker(int size = 1000, int overlap = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, size);
        Size = size;
        Overlap = overlap;
    }

    /// <summary>Largest chunk, in characters.</summary>
    public int Size { get; }

    /// <summary>Characters shared by consecutive chunks (approximately: boundaries snap to words).</summary>
    public int Overlap { get; }

    public string Name => $"fixed-{Size}-{Overlap}";

    public IReadOnlyList<Chunk> Split(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = document.Text;
        var chunks = new List<Chunk>();
        var start = SkipWhitespace(text, 0);
        while (start < text.Length)
        {
            var end = Math.Min(start + Size, text.Length);
            if (end < text.Length)
            {
                end = SnapEnd(text, start, end);
            }

            var trimmedEnd = TrimEnd(text, start, end);
            chunks.Add(ChunkFactory.Create(document, chunks.Count, start, trimmedEnd));
            if (end >= text.Length)
            {
                break;
            }

            // Next window starts Overlap characters before this end, at a word start, and always moves forward.
            var next = SnapStart(text, Math.Max(end - Overlap, start + 1), end);
            start = SkipWhitespace(text, next <= start ? end : next);
        }

        return chunks;
    }

    /// <summary>Moves a cut back to just after the last whitespace in the second half of the window.</summary>
    private static int SnapEnd(string text, int start, int end)
    {
        if (char.IsWhiteSpace(text[end]) || char.IsWhiteSpace(text[end - 1]))
        {
            return end;
        }

        var floor = start + ((end - start) / 2);
        for (var i = end - 1; i > floor; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return end;   // one very long word: cut it
    }

    /// <summary>Moves a start forward to the beginning of the next word (not past <paramref name="limit"/>).</summary>
    private static int SnapStart(string text, int start, int limit)
    {
        if (start == 0 || char.IsWhiteSpace(text[start - 1]))
        {
            return start;
        }

        for (var i = start; i < limit; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i + 1;
            }
        }

        return start;
    }

    private static int SkipWhitespace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    private static int TrimEnd(string text, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return end;
    }
}

/// <summary>Builds chunks with their source span and section.</summary>
public static class ChunkFactory
{
    public static Chunk Create(Document document, int index, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Chunk(document.Id, index, document.Text[start..end], start, end, document.SpanOf(start, end),
            SectionAt(document, start));
    }

    /// <summary>The heading path in effect at a text offset, e.g. "Install › Docker".</summary>
    public static string? SectionAt(Document document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = new List<Heading>();
        foreach (var heading in document.Headings)
        {
            if (heading.TextOffset > offset)
            {
                break;
            }

            while (path.Count > 0 && path[^1].Level >= heading.Level)
            {
                path.RemoveAt(path.Count - 1);
            }

            path.Add(heading);
        }

        return path.Count == 0 ? null : string.Join(" › ", path.Select(h => h.Title));
    }
}
