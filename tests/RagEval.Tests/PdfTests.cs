using RagEval.Core.Chunking;
using RagEval.Core.Documents;
using RagEval.Ingestion;
using RagEval.Ingestion.Loaders;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace RagEval.Tests;

public sealed class PdfTests
{
    /// <summary>A PDF with one line of text per entry; null leaves a paragraph gap.</summary>
    internal static byte[] Pdf(string? title, IReadOnlyList<string?[]> pages, params (string Title, int Level, int Page)[] bookmarks)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780.0;
            foreach (var line in lines)
            {
                if (line is not null)
                {
                    page.AddText(line, 11, new PdfPoint(60, y), font);
                }

                y -= line is null ? 16 : 14;
            }
        }

        if (title is not null)
        {
            builder.DocumentInformation.Title = title;
        }

        if (bookmarks.Length > 0)
        {
            builder.Bookmarks = new Bookmarks([.. bookmarks.Select(b => (BookmarkNode)new DocumentBookmarkNode(b.Title, b.Level,
                new ExplicitDestination(b.Page, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty), []))]);
        }

        return builder.Build();
    }

    private static readonly byte[] Guide = Pdf("Field guide",
        [
            ["Introduction", "Owls hunt mostly at night and rest", "during the day.", null, "They swallow prey whole."],
            ["Habitat", "Barn owls nest in old build-", "ings and hollow trees.", null, "Some species live in deserts."],
        ],
        ("Introduction", 0, 1), ("Habitat", 0, 2));

    [Fact]
    public void Extracts_text_per_page_with_paragraphs_and_joined_hyphens()
    {
        var doc = new PdfLoader().Load("guide.pdf", "guide.pdf", Guide);

        Assert.Equal(DocumentFormat.Pdf, doc.Format);
        Assert.Equal("Field guide", doc.Title);
        Assert.Contains("Owls hunt mostly at night and rest during the day.", doc.Text, StringComparison.Ordinal);
        Assert.Contains("nest in old buildings and hollow trees.", doc.Text, StringComparison.Ordinal);
        Assert.Contains("during the day.\n\nThey swallow prey whole.", doc.Text, StringComparison.Ordinal);
        Assert.Equal(1, doc.Map.PageOf(doc.Text.IndexOf("swallow", StringComparison.Ordinal)));
        Assert.Equal(2, doc.Map.PageOf(doc.Text.IndexOf("deserts", StringComparison.Ordinal)));
    }

    [Fact]
    public void Bookmarks_become_headings_and_chunks_cite_their_page()
    {
        var doc = new PdfLoader().Load("guide.pdf", "guide.pdf", Guide);

        Assert.Equal(["Introduction", "Habitat"], doc.Headings.Select(h => h.Title));
        Assert.All(doc.Headings, h => Assert.Equal(1, h.Level));
        Assert.Equal(doc.Text.IndexOf("Habitat", StringComparison.Ordinal), doc.Headings[1].TextOffset);

        var chunks = new StructuralChunker(150).Split(doc);    // small enough that the two short sections are not joined
        var habitat = Assert.Single(chunks, c => c.Text.Contains("deserts", StringComparison.Ordinal));
        Assert.Equal(2, habitat.Source.Page);
        Assert.Equal("Habitat", habitat.Section);
        Assert.Equal(1, Assert.Single(chunks, c => c.Text.Contains("swallow", StringComparison.Ordinal)).Source.Page);
    }

    [Fact]
    public void Source_offsets_point_into_the_extracted_page_text()
    {
        var doc = new PdfLoader().Load("guide.pdf", "guide.pdf", Guide);
        // Every non-space character maps to the same character in the source text (pages joined by form feeds).
        var builder = new System.Text.StringBuilder();
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(Guide))
        {
            foreach (var page in pdf.GetPages())
            {
                if (page.Number > 1)
                {
                    builder.Append('\f');
                }

                builder.Append(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page, true)
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
            }
        }

        var original = builder.ToString();
        for (var i = 0; i < doc.Text.Length; i++)
        {
            if (!char.IsWhiteSpace(doc.Text[i]))
            {
                Assert.Equal(doc.Text[i], original[doc.Map.SourceOffset(i)]);
            }
        }
    }

    [Fact]
    public async Task Reader_loads_pdfs_from_folders_and_text_as_before()
    {
        var root = Directory.CreateTempSubdirectory("rag-eval-pdf-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "guide.pdf"), Guide, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(root, "bom.md"), [0xEF, 0xBB, 0xBF, .. "# Başlık\n\nİçerik."u8.ToArray()],
                TestContext.Current.CancellationToken);
            var docs = new List<Document>();
            await foreach (var d in DocumentReader.Default().ReadFolderAsync(root, TestContext.Current.CancellationToken))
            {
                docs.Add(d);
            }

            Assert.Equal(["bom.md", "guide.pdf"], docs.Select(d => d.Id));
            Assert.Equal("Başlık", docs[0].Title);
            Assert.StartsWith("Başlık", docs[0].Text, StringComparison.Ordinal);
            Assert.Equal(DocumentFormat.Pdf, docs[1].Format);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Pdfs_without_text_or_title_still_load()
    {
        var empty = new PdfLoader().Load("scan.pdf", "scan.pdf", Pdf(null, [[]]));
        Assert.Equal("", empty.Text);
        Assert.Null(empty.Title);
        Assert.Empty(new StructuralChunker(1000).Split(empty));

        Assert.ThrowsAny<Exception>(() => new PdfLoader().Load("bad.pdf", "bad.pdf", "not a pdf"u8.ToArray()));
        Assert.Throws<NotSupportedException>(() => new PdfLoader().Load("x.pdf", "x.pdf", "text"));
    }
}
