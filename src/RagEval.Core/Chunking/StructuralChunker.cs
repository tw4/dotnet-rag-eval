using RagEval.Core.Documents;

namespace RagEval.Core.Chunking;

/// <summary>
/// Recursive splitting by structure: a chunk never crosses a section boundary (heading); a section
/// longer than <see cref="Size"/> is split at paragraphs, a paragraph at sentences, a sentence at
/// words, and the resulting pieces are merged back while they fit. Sections much shorter than
/// <see cref="Size"/> (a heading followed directly by a sub-heading) are joined to the next one.
/// </summary>
public sealed class StructuralChunker : IChunker
{
    public StructuralChunker(int size = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 20);
        Size = size;
    }

    /// <summary>Largest chunk, in characters.</summary>
    public int Size { get; }

    public string Name => $"structural-{Size}";

    public IReadOnlyList<Chunk> Split(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = document.Text;
        var pieces = new List<(int Start, int End)>();
        foreach (var (start, end) in Sections(document))
        {
            var sectionPieces = new List<(int, int)>();
            SplitRange(text, start, end, Level.Paragraph, sectionPieces);
            pieces.AddRange(Merge(sectionPieces));
        }

        var chunks = new List<Chunk>(pieces.Count);
        foreach (var (start, end) in pieces)
        {
            chunks.Add(ChunkFactory.Create(document, chunks.Count, start, end));
        }

        return chunks;
    }

    private enum Level
    {
        Paragraph,
        Sentence,
        Word,
    }

    /// <summary>Section ranges, with very short sections joined to the following one.</summary>
    private IEnumerable<(int Start, int End)> Sections(Document document)
    {
        var bounds = new List<int> { 0 };
        bounds.AddRange(document.Headings.Select(h => h.TextOffset).Where(o => o > 0 && o < document.Text.Length));
        bounds.Add(document.Text.Length);
        bounds = [.. bounds.Distinct().Order()];

        var start = bounds[0];
        for (var i = 1; i < bounds.Count; i++)
        {
            var end = bounds[i];
            var isLast = i == bounds.Count - 1;
            var length = TrimmedLength(document.Text, start, end);
            var nextLength = isLast ? 0 : TrimmedLength(document.Text, end, bounds[i + 1]);
            if (!isLast && length < Size / 5 && length + nextLength <= Size)
            {
                continue;   // too short on its own: carry into the next section
            }

            yield return (start, end);
            start = end;
        }
    }

    private void SplitRange(string text, int start, int end, Level level, List<(int, int)> output)
    {
        var (s, e) = Trimmed(text, start, end);
        if (e <= s)
        {
            return;
        }

        if (e - s <= Size)
        {
            output.Add((s, e));
            return;
        }

        switch (level)
        {
            case Level.Paragraph:
                var at = s;
                while (at < e)
                {
                    var brk = text.IndexOf("\n\n", at, e - at, StringComparison.Ordinal);
                    var paragraphEnd = brk < 0 ? e : brk;
                    SplitRange(text, at, paragraphEnd, Level.Sentence, output);
                    at = brk < 0 ? e : brk + 2;
                }

                break;
            case Level.Sentence:
                var sentences = SentenceSplitter.Split(text[s..e]);
                if (sentences.Count <= 1)
                {
                    SplitRange(text, s, e, Level.Word, output);
                    break;
                }

                foreach (var (ss, se) in sentences)
                {
                    SplitRange(text, s + ss, s + se, Level.Word, output);
                }

                break;
            default:
                // A single sentence longer than Size: fall back to word-aligned windows without overlap.
                var builder = new TextBuilder();
                builder.Append(text.AsSpan(s, e - s), 0);
                var temporary = builder.Build("_", "_", DocumentFormat.Text, null, text[s..e]);
                foreach (var piece in new FixedSizeChunker(Size, 0).Split(temporary))
                {
                    output.Add((s + piece.TextStart, s + piece.TextEnd));
                }

                break;
        }
    }

    private static int TrimmedLength(string text, int start, int end)
    {
        var (s, e) = Trimmed(text, start, end);
        return e - s;
    }

    /// <summary>Joins consecutive pieces while the joined range fits in <see cref="Size"/>.</summary>
    private List<(int Start, int End)> Merge(List<(int Start, int End)> pieces)
    {
        var merged = new List<(int, int)>();
        foreach (var piece in pieces)
        {
            if (merged.Count > 0 && piece.End - merged[^1].Item1 <= Size)
            {
                merged[^1] = (merged[^1].Item1, piece.End);
            }
            else
            {
                merged.Add(piece);
            }
        }

        return merged;
    }

    private static (int Start, int End) Trimmed(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return (start, end);
    }
}
