using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Ingestion.Loaders;

namespace RagEval.Tests;

public class ChunkingTests
{
    private static Document Doc(string text)
    {
        var builder = new TextBuilder();
        builder.Append(text, 0);
        return builder.Build("doc", "doc", DocumentFormat.Text, null, text);
    }

    private static string RandomText(Random random, int words)
    {
        var parts = new string[words];
        for (var i = 0; i < words; i++)
        {
            var length = random.Next(1, 12);
            parts[i] = new string([.. Enumerable.Range(0, length).Select(_ => (char)('a' + random.Next(26)))]);
        }

        return string.Join(' ', parts);
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(50, 0)]
    [InlineData(300, 150)]
    [InlineData(10, 9)]
    public void Chunks_cover_the_text_without_cutting_words_or_exceeding_the_size(int size, int overlap)
    {
        var random = new Random(size * 31 + overlap);
        for (var round = 0; round < 50; round++)
        {
            var doc = Doc(RandomText(random, random.Next(1, 400)));
            var chunks = new FixedSizeChunker(size, overlap).Split(doc);

            Assert.NotEmpty(chunks);
            Assert.Equal(0, chunks[0].TextStart);
            Assert.Equal(doc.Text.Length, chunks[^1].TextEnd);
            for (var i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                Assert.Equal(i, c.Index);
                Assert.Equal(doc.Text[c.TextStart..c.TextEnd], c.Text);
                Assert.True(c.Text.Length <= size, $"chunk {i} has {c.Text.Length} > {size} chars");
                Assert.False(char.IsWhiteSpace(c.Text[0]) || char.IsWhiteSpace(c.Text[^1]));
                // No word cut: boundaries sit at the text edges or next to whitespace (words here are ≤ 11 chars < size).
                if (size > 11)
                {
                    Assert.True(c.TextStart == 0 || char.IsWhiteSpace(doc.Text[c.TextStart - 1]));
                    Assert.True(c.TextEnd == doc.Text.Length || char.IsWhiteSpace(doc.Text[c.TextEnd]));
                }

                if (i > 0)
                {
                    var previous = chunks[i - 1];
                    Assert.True(c.TextStart > previous.TextStart, "chunks must move forward");
                    Assert.True(c.TextStart <= previous.TextEnd + 1, $"gap between chunk {i - 1} and {i}");
                }
            }
        }
    }

    [Fact]
    public void Overlap_repeats_the_end_of_the_previous_chunk()
    {
        var doc = Doc(string.Join(' ', Enumerable.Range(0, 200).Select(i => $"w{i:000}")));   // words of 4 chars
        var chunks = new FixedSizeChunker(100, 30).Split(doc);

        for (var i = 1; i < chunks.Count; i++)
        {
            var shared = chunks[i - 1].TextEnd - chunks[i].TextStart;
            Assert.InRange(shared, 25, 30);
        }
    }

    [Fact]
    public void Short_and_empty_documents()
    {
        Assert.Single(new FixedSizeChunker(100, 10).Split(Doc("just a few words")));
        Assert.Empty(new FixedSizeChunker(100, 10).Split(Doc("   ")));
    }

    [Fact]
    public void A_word_longer_than_the_window_is_cut()
    {
        var chunks = new FixedSizeChunker(10, 2).Split(Doc(new string('x', 25)));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 10));
        Assert.Equal(25, chunks[^1].TextEnd);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 100)]
    [InlineData(100, -1)]
    public void Invalid_settings_are_rejected(int size, int overlap) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new FixedSizeChunker(size, overlap));

    [Fact]
    public void Chunks_carry_their_source_span_and_section()
    {
        const string md = "# Guide\n\nIntro text here.\n\n## Install\n\nRun the installer and follow the steps on screen.\n";
        var doc = new MarkdownLoader().Load("guide.md", "guide.md", md);
        var chunks = new FixedSizeChunker(40, 10).Split(doc);

        var install = chunks.First(c => c.Text.Contains("installer", StringComparison.Ordinal));
        Assert.Equal("Guide › Install", install.Section);
        Assert.Equal(md.IndexOf(install.Text[..10], StringComparison.Ordinal), install.Source.Start);
        Assert.Equal(7, install.Source.StartLine);
        Assert.Equal("guide.md#" + install.Index, install.Id);
        Assert.Equal("fixed-40-10", new FixedSizeChunker(40, 10).Name);
    }
}
