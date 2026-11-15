using RagEval.Core.Documents;
using RagEval.Ingestion;
using RagEval.Ingestion.Loaders;

namespace RagEval.Tests;

public class IngestionTests
{
    private const string Markdown = """
        ---
        title: ignored front matter
        ---
        # Getting started

        Install the **gateway** with [Docker](https://docker.com) and run `docker compose up`.
        Then *open* the dashboard &amp; log in.

        ## Configuration

        - First item
        - Second item with `code`

        | Key | Value |
        |-----|-------|
        | port | 8080 |

        ```bash
        echo hello
        ```
        """;

    private const string Html = """
        <!doctype html>
        <html><head><title>The Page</title><style>p { color: red }</style></head>
        <body>
          <nav hidden>skip me</nav>
          <h1>Welcome</h1>
          <p>Fish &amp; chips are <b>tasty</b>.</p>
          <script>var p = "Welcome";</script>
          <h2>Details</h2>
          <ul><li>one</li><li>two</li></ul>
        </body></html>
        """;

    [Fact]
    public void Markdown_text_drops_markup_and_keeps_structure()
    {
        var doc = new MarkdownLoader().Load("guide.md", "guide.md", Markdown);

        Assert.Equal("Getting started", doc.Title);
        Assert.StartsWith("Getting started\n\nInstall the gateway with Docker and run docker compose up. Then open the dashboard & log in.", doc.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", doc.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://docker.com", doc.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("front matter", doc.Text, StringComparison.Ordinal);
        Assert.Contains("Second item with code", doc.Text, StringComparison.Ordinal);
        Assert.Contains("port", doc.Text, StringComparison.Ordinal);
        Assert.Contains("echo hello", doc.Text, StringComparison.Ordinal);
        Assert.Equal([(1, "Getting started"), (2, "Configuration")], doc.Headings.Select(h => (h.Level, h.Title)));
        Assert.All(doc.Headings, h => Assert.StartsWith(h.Title, doc.Text[h.TextOffset..], StringComparison.Ordinal));
    }

    [Fact]
    public void Html_text_skips_scripts_styles_and_hidden_elements()
    {
        var doc = new HtmlLoader().Load("page.html", "page.html", Html);

        Assert.Equal("The Page", doc.Title);
        Assert.Equal("Welcome\n\nFish & chips are tasty.\n\nDetails\n\none\n\ntwo", doc.Text);
        Assert.Equal([(1, "Welcome"), (2, "Details")], doc.Headings.Select(h => (h.Level, h.Title)));
        Assert.All(doc.Headings, h => Assert.StartsWith(h.Title, doc.Text[h.TextOffset..], StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("md")]
    [InlineData("html")]
    public void Every_character_maps_back_to_the_same_character_in_the_source(string format)
    {
        var (source, doc) = format == "md"
            ? (Markdown, new MarkdownLoader().Load("a", "a", Markdown))
            : (Html, new HtmlLoader().Load("a", "a", Html));

        for (var i = 0; i < doc.Text.Length; i++)
        {
            var c = doc.Text[i];
            if (char.IsWhiteSpace(c))
            {
                continue;   // separators the extractor inserted
            }

            Assert.True(source[doc.Map.SourceOffset(i)] == c,
                $"text[{i}] = '{c}' maps to source[{doc.Map.SourceOffset(i)}] = '{source[doc.Map.SourceOffset(i)]}'");
        }
    }

    [Fact]
    public void Highlighted_code_maps_text_not_attribute_values()
    {
        // Syntax highlighters wrap single characters in spans whose attributes contain the same
        // characters; numeric entities decode to one character that maps to its '&'.
        const string html = """<pre><span class="pl-pds">"</span>llama3.2<span class="pl-pds">"</span> &#34;x&#34; &lt;-- y</pre>""";
        var doc = new HtmlLoader().Load("c", "c", html);

        Assert.Equal("\"llama3.2\" \"x\" <-- y", doc.Text);
        for (var i = 0; i < doc.Text.Length; i++)
        {
            if (char.IsWhiteSpace(doc.Text[i]))
            {
                continue;
            }

            var s = html[doc.Map.SourceOffset(i)];
            Assert.True(s == doc.Text[i] || s == '&', $"text[{i}] = '{doc.Text[i]}' maps to '{s}'");
            Assert.False(html.LastIndexOf('<', doc.Map.SourceOffset(i)) > html.LastIndexOf('>', doc.Map.SourceOffset(i)),
                $"text[{i}] maps inside a tag");
        }
    }

    [Fact]
    public void A_text_range_cites_its_source_span_and_lines()
    {
        var doc = new MarkdownLoader().Load("guide.md", "guide.md", Markdown);
        var start = doc.Text.IndexOf("Configuration", StringComparison.Ordinal);

        var span = doc.SpanOf(start, start + "Configuration".Length);

        Assert.Equal("Configuration", Markdown[span.Start..span.End]);
        Assert.Equal(9, span.StartLine);
        Assert.Equal(9, span.EndLine);
        Assert.Null(span.Page);
    }

    [Fact]
    public async Task Reader_loads_supported_files_with_relative_ids()
    {
        var root = Directory.CreateTempSubdirectory("rag-eval-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "guide.md"), Markdown, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "page.html"), Html, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "notes.bin"), "ignored", TestContext.Current.CancellationToken);

            var docs = new List<Document>();
            await foreach (var d in DocumentReader.Default().ReadFolderAsync(root, TestContext.Current.CancellationToken))
            {
                docs.Add(d);
            }

            Assert.Equal(["docs/guide.md", "page.html"], docs.Select(d => d.Id));
            Assert.Equal([DocumentFormat.Markdown, DocumentFormat.Html], docs.Select(d => d.Format));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
