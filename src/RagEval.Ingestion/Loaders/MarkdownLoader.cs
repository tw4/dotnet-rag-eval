using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using RagEval.Core.Documents;

namespace RagEval.Ingestion.Loaders;

/// <summary>
/// Extracts the text of a Markdown file with Markdig. Every character keeps the offset it has
/// in the file; markup (emphasis markers, link targets, images, HTML) is dropped.
/// </summary>
public sealed class MarkdownLoader : IDocumentLoader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseGridTables()
        .Build();

    public IReadOnlyCollection<string> Extensions { get; } = [".md", ".markdown"];

    public Document Load(string id, string source, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var markdown = Markdown.Parse(content, Pipeline);
        var builder = new TextBuilder();
        string? title = null;
        WalkBlocks(markdown, builder, content, ref title);
        return builder.Build(id, source, DocumentFormat.Markdown, title, content);
    }

    private static void WalkBlocks(ContainerBlock container, TextBuilder builder, string content, ref string? title)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case YamlFrontMatterBlock or ThematicBreakBlock or LinkReferenceDefinitionGroup or HtmlBlock:
                    break;
                case HeadingBlock heading:
                    var text = InlineText(heading.Inline, content);
                    if (title is null && heading.Level == 1)
                    {
                        title = text;
                    }

                    builder.BeginHeading(heading.Level, text);
                    WalkInlines(heading.Inline, builder, content);
                    builder.BreakBlock();
                    break;
                case CodeBlock code:   // fenced and indented
                    builder.BreakBlock();
                    foreach (var line in code.Lines.Lines.AsSpan(0, code.Lines.Count))
                    {
                        // Position is the line's offset in the file; the slice may start after indentation.
                        var offset = line.Position + (line.Slice.Start - line.Slice.Text.LastIndexOf('\n', Math.Max(0, line.Slice.Start - 1)) - 1);
                        builder.Append(line.Slice.AsSpan(), ReferenceEquals(line.Slice.Text, content) ? line.Slice.Start : offset);
                        builder.AppendChar(' ', line.Position);
                    }

                    builder.BreakBlock();
                    break;
                case LeafBlock leaf:   // paragraphs, table cells' paragraphs, …
                    builder.BreakBlock();
                    WalkInlines(leaf.Inline, builder, content);
                    builder.BreakBlock();
                    break;
                case ContainerBlock child:   // lists, list items, quotes, tables
                    WalkBlocks(child, builder, content, ref title);
                    builder.BreakBlock();
                    break;
            }
        }
    }

    private static void WalkInlines(ContainerInline? container, TextBuilder builder, string content)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    // Span is the position in the file; Content.Start may be relative to a buffer
                    // Markdig builds for multi-line paragraphs.
                    if (literal.Span.Length == literal.Content.Length)
                    {
                        builder.Append(literal.Content.AsSpan(), literal.Span.Start);
                    }
                    else
                    {
                        builder.AppendAligned(literal.Content.AsSpan(), content, literal.Span.Start, literal.Span.End + 1);
                    }

                    break;
                case CodeInline code:
                    builder.AppendAligned(code.Content, content, code.Span.Start + code.DelimiterCount, code.Span.End + 1);
                    break;
                case HtmlEntityInline entity:
                    builder.AppendAligned(entity.Transcoded.AsSpan(), content, entity.Span.Start, entity.Span.End + 1);
                    break;
                case AutolinkInline link:
                    builder.Append(link.Url, link.Span.Start + 1);
                    break;
                case LineBreakInline lineBreak:
                    builder.AppendChar(' ', lineBreak.Span.Start);
                    break;
                case LinkInline { IsImage: true }:
                    break;   // images carry no searchable text
                case ContainerInline child:   // emphasis, links: keep their text, drop the markup
                    WalkInlines(child, builder, content);
                    break;
                    // HtmlInline and anything else: markup only
            }
        }
    }

    private static string InlineText(ContainerInline? container, string content)
    {
        var builder = new TextBuilder();
        WalkInlines(container, builder, content);
        return builder.Build("_", "_", DocumentFormat.Text, null, string.Empty).Text;
    }
}
