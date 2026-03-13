using System.Linq;
using System.Threading.Tasks;
using Safetch.Core.Processing;
using Xunit;

namespace Safetch.Tests.Processing;

/// <summary>
/// Integration tests for the markdown mode processor pipeline:
/// ReadableContentProcessor → HtmlSanitizerProcessor → HtmlToMarkdownProcessor
/// </summary>
public class MarkdownModeIntegrationTests
{
    private static ContentProcessorPipeline BuildMarkdownPipeline()
    {
        var processors = new OrderedProcessor[]
        {
            new OrderedProcessor(1, "text/html+markdown", new ReadableContentProcessor()),
            new OrderedProcessor(2, "text/html+markdown", new HtmlSanitizerProcessor()),
            new OrderedProcessor(3, "text/html+markdown", new HtmlToMarkdownProcessor())
        };
        return new ContentProcessorPipeline(processors);
    }

    private static ProcessingContext MarkdownCtx(string url = "https://example.com")
        => new ProcessingContext("text/html+markdown", url);

    private static string GenerateLongBody()
    {
        const string sentence = "This article discusses important topics in software engineering, architecture, and best practices for building scalable systems. ";
        return string.Concat(Enumerable.Repeat(sentence, 20));
    }

    private static string BuildReadableHtml(string title, string body) => $@"<!DOCTYPE html>
<html lang=""en"">
<head><title>{title}</title></head>
<body>
  <header><nav><ul><li><a href=""/"">Home</a></li><li><a href=""/about"">About</a></li></ul></nav></header>
  <main>
    <article>
      <h1>{title}</h1>
      <p>{body}</p>
      <p>{body}</p>
      <p>{body}</p>
      <p>{body}</p>
      <p>{body}</p>
    </article>
  </main>
  <footer><p>Copyright 2025</p></footer>
</body>
</html>";

    [Fact]
    public async Task MarkdownPipeline_ArticleHtml_ReturnsNonEmptyContent()
    {
        var pipeline = BuildMarkdownPipeline();
        var html = BuildReadableHtml("Test Article", GenerateLongBody());

        var result = await pipeline.RunAsync(html, MarkdownCtx(), default);

        Assert.NotEmpty(result.Content);
    }

    [Fact]
    public async Task MarkdownPipeline_ArticleHtml_DoesNotContainRawHtmlTags()
    {
        var pipeline = BuildMarkdownPipeline();
        var html = BuildReadableHtml("Test Article", GenerateLongBody());

        var result = await pipeline.RunAsync(html, MarkdownCtx(), default);

        // Whether readable extraction succeeds or falls back, HtmlToMarkdownProcessor
        // should convert HTML to Markdown — no raw block-level tags should remain
        Assert.DoesNotContain("<nav>", result.Content);
        Assert.DoesNotContain("<footer>", result.Content);
    }

    [Fact]
    public async Task MarkdownPipeline_ArticleHtml_HandlesReadableAndNonReadablePaths()
    {
        var pipeline = BuildMarkdownPipeline();
        var html = BuildReadableHtml("Test Article", GenerateLongBody());

        var result = await pipeline.RunAsync(html, MarkdownCtx(), default);

        // Either readable extraction worked (markdown with # headings) or it fell back
        // (Markdown from full HTML). Either way: non-empty, no raw <h1> block tags remaining.
        Assert.NotEmpty(result.Content);
        Assert.DoesNotContain("<h1>", result.Content);
    }

    [Fact]
    public async Task MarkdownPipeline_ShortHtml_StillReturnsContent()
    {
        var pipeline = BuildMarkdownPipeline();
        // Short page — SmartReader will likely mark as non-readable — fallback path
        var html = "<html><body><p>Short content.</p></body></html>";

        var result = await pipeline.RunAsync(html, MarkdownCtx(), default);

        Assert.NotNull(result.Content);
    }
}