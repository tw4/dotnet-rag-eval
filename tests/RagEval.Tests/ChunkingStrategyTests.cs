using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Core.Indexing;
using RagEval.Ingestion.Loaders;

namespace RagEval.Tests;

public class ChunkingStrategyTests
{
    private static string[] Sentences(string text) => [.. SentenceSplitter.Split(text).Select(s => text[s.Start..s.End])];

    [Fact]
    public void Splits_english_sentences_but_not_at_abbreviations_numbers_or_initials()
    {
        Assert.Equal(
            ["Smith et al. reported a 3.5-fold increase, e.g. in mice.", "Dr. J. Watson disagreed!", "Why?", "\"Because,\" he said."],
            Sentences("Smith et al. reported a 3.5-fold increase, e.g. in mice. Dr. J. Watson disagreed! Why? \"Because,\" he said."));
    }

    [Fact]
    public void Splits_turkish_sentences_but_not_at_ordinals_or_abbreviations()
    {
        Assert.Equal(
            ["Prof. Dr. Ayşe Kaya 3. yüzyılda yazılan metinleri inceledi.", "Şeker, tuz vb. maddeler zararlıdır.", "İnsülin 1921'de keşfedildi."],
            Sentences("Prof. Dr. Ayşe Kaya 3. yüzyılda yazılan metinleri inceledi. Şeker, tuz vb. maddeler zararlıdır. İnsülin 1921'de keşfedildi."));
    }

    [Fact]
    public void Paragraph_breaks_end_sentences()
    {
        Assert.Equal(["Heading without period", "A line with no final period", "Second paragraph."],
            Sentences("Heading without period\n\nA line with no final period\n\nSecond paragraph."));
    }

    private static Document Markdown(int seed)
    {
        var random = new Random(seed);
        string Sentence() => string.Join(' ', Enumerable.Range(0, random.Next(4, 30)).Select(_ => "w" + random.Next(1000))) + ".";
        string Paragraph() => string.Join(' ', Enumerable.Range(0, random.Next(1, 12)).Select(_ => Sentence()));
        var md = new System.Text.StringBuilder();
        for (var h = 0; h < random.Next(1, 8); h++)
        {
            md.Append(new string('#', random.Next(1, 4))).Append(" Heading ").Append(h).Append("\n\n");
            for (var p = 0; p < random.Next(0, 6); p++)
            {
                md.Append(Paragraph()).Append("\n\n");
            }
        }

        return new MarkdownLoader().Load("doc", "doc", md.ToString());
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1000)]
    public void Structural_chunks_fit_cover_the_text_and_stay_inside_sections(int size)
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var doc = Markdown(seed);
            var chunks = new StructuralChunker(size).Split(doc);
            var covered = new bool[doc.Text.Length];
            foreach (var c in chunks)
            {
                Assert.True(c.Text.Length <= size, $"seed {seed}: chunk of {c.Text.Length} > {size}");
                Assert.Equal(doc.Text[c.TextStart..c.TextEnd], c.Text);
                for (var i = c.TextStart; i < c.TextEnd; i++)
                {
                    covered[i] = true;
                }

                // Heading boundaries strictly inside a chunk belong to short sections merged forward:
                // at most one such boundary per chunk, and only when the text before it is short.
                var inside = doc.Headings.Where(h => h.TextOffset > c.TextStart && h.TextOffset < c.TextEnd).ToList();
                foreach (var h in inside)
                {
                    Assert.True(h.TextOffset - c.TextStart < size / 5 + 2 || inside.Count == 0,
                        $"seed {seed}: chunk crosses a long section at {h.TextOffset}");
                }
            }

            for (var i = 0; i < doc.Text.Length; i++)
            {
                Assert.True(covered[i] || char.IsWhiteSpace(doc.Text[i]), $"seed {seed}: text[{i}] not in any chunk");
            }

            Assert.True(chunks.Zip(chunks.Skip(1)).All(p => p.First.TextEnd <= p.Second.TextStart), "chunks must not overlap");
        }
    }

    [Fact]
    public void Structural_chunks_start_at_headings()
    {
        var doc = new MarkdownLoader().Load("d", "d", "# Guide\n\nIntro paragraph that is long enough to stand alone as a section of its own here.\n\n## Install\n\nRun the installer.\n\n## Use\n\nOpen the app and log in with your account details please.\n");
        var chunks = new StructuralChunker(80).Split(doc);
        Assert.Contains(chunks, c => c.Text.StartsWith("Install", StringComparison.Ordinal) && c.Section == "Guide › Install");
        Assert.Contains(chunks, c => c.Text.StartsWith("Use", StringComparison.Ordinal));
        Assert.Equal("structural-80", new StructuralChunker(80).Name);
    }

    [Fact]
    public void Sentence_windows_embed_one_sentence_and_return_its_neighbours()
    {
        var builder = new TextBuilder();
        const string text = "One. Two. Three. Four. Five.";
        builder.Append(text, 0);
        var doc = builder.Build("d", "d", DocumentFormat.Text, null, text);

        var chunks = new SentenceWindowChunker(1).Split(doc);

        Assert.Equal(5, chunks.Count);
        Assert.Equal(("One.", "One. Two."), (chunks[0].TextToEmbed, chunks[0].Text));
        Assert.Equal(("Three.", "Two. Three. Four."), (chunks[2].TextToEmbed, chunks[2].Text));
        Assert.Equal(("Five.", "Four. Five."), (chunks[4].TextToEmbed, chunks[4].Text));
        Assert.Equal("sentence-window-1", new SentenceWindowChunker(1).Name);

        var back = ChunkStore.FromPayload(ChunkStore.ToPoint(doc, chunks[2], new float[] { 1 }).Payload, 1);
        Assert.Equal(chunks[2], back.Chunk);
    }
}
