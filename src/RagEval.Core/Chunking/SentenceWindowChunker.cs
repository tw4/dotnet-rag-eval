using RagEval.Core.Documents;

namespace RagEval.Core.Chunking;

/// <summary>
/// One chunk per sentence: the sentence alone is embedded, so the vector is specific, but the chunk's
/// text, what retrieval returns and the model reads, is the window of <see cref="Window"/> sentences
/// on each side (within the same paragraph-free run of text).
/// </summary>
public sealed class SentenceWindowChunker : IChunker
{
    public SentenceWindowChunker(int window = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(window);
        Window = window;
    }

    /// <summary>Sentences of context on each side of the embedded sentence.</summary>
    public int Window { get; }

    public string Name => $"sentence-window-{Window}";

    public IReadOnlyList<Chunk> Split(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sentences = SentenceSplitter.Split(document.Text);
        var chunks = new List<Chunk>(sentences.Count);
        for (var i = 0; i < sentences.Count; i++)
        {
            var (start, end) = sentences[i];
            var windowStart = sentences[Math.Max(0, i - Window)].Start;
            var windowEnd = sentences[Math.Min(sentences.Count - 1, i + Window)].End;
            var window = ChunkFactory.Create(document, chunks.Count, windowStart, windowEnd);
            chunks.Add(window with
            {
                EmbeddingText = document.Text[start..end],
                Section = ChunkFactory.SectionAt(document, start),
            });
        }

        return chunks;
    }
}
