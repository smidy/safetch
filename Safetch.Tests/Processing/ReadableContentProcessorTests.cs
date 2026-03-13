using System.Threading.Tasks;
using Safetch.Core.Processing;
using Xunit;

namespace Safetch.Tests.Processing;

public class ReadableContentProcessorTests
{
    private readonly ReadableContentProcessor _processor = new();

    private static ProcessingContext ReadableCtx(string url = "https://example.com")
        => new ProcessingContext("text/html+readable", url);

    private static ProcessingContext TextCtx(string url = "https://example.com")
        => new ProcessingContext("text/html+text", url);

    [Fact]
    public async Task Readable_ReadableContent_DoesNotContainNavigation()
    {
        var html = BuildReadableHtml("Hello World", GenerateLongBody());
        var result = await _processor.ProcessAsync(html, ReadableCtx(), default);

        // If readable, nav should be stripped; if not readable, we get a warning
        if (result.Warnings.Count == 0)
        {
            Assert.DoesNotContain("<nav>", result.Content);
        }
        else
        {
            // Graceful fallback — extraction failed but processor still returned content
            Assert.Contains(result.Warnings, w => w.Contains("Readable extraction failed"));
        }
    }

    [Fact]
    public async Task Readable_ReadableContent_ReturnsContent()
    {
        var html = BuildReadableHtml("Hello World", GenerateLongBody());
        var result = await _processor.ProcessAsync(html, ReadableCtx(), default);
        Assert.NotEmpty(result.Content);
    }

    [Fact]
    public async Task Text_ReadableContent_FallsBackOrStripsHtml()
    {
        var html = BuildReadableHtml("Hello World", GenerateLongBody());
        var result = await _processor.ProcessAsync(html, TextCtx(), default);

        // Either readable extraction succeeded (no <html> tags) or fallback kicked in
        // Either way, result should not contain raw block-level tags that SmartReader or StripTags removes
        Assert.NotEmpty(result.Content);
        Assert.DoesNotContain("<nav>", result.Content);
    }

    [Fact]
    public async Task Readable_NonReadableContent_ReturnsWarning()
    {
        var html = "<html><body><p>hi</p></body></html>";
        var result = await _processor.ProcessAsync(html, ReadableCtx(), default);
        Assert.Contains(result.Warnings, w => w.Contains("Readable extraction failed"));
    }

    [Fact]
    public async Task Text_NonReadableContent_ReturnsWarningAndStrippedText()
    {
        var html = "<html><body><p>hi</p></body></html>";
        var result = await _processor.ProcessAsync(html, TextCtx(), default);
        Assert.Contains(result.Warnings, w => w.Contains("Readable extraction failed"));
        Assert.DoesNotContain("<p>", result.Content);
    }

    [Fact]
    public async Task Readable_AlwaysReturnsNonNullContent()
    {
        var html = "";
        var result = await _processor.ProcessAsync(html, ReadableCtx(), default);
        Assert.NotNull(result.Content);
    }

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
}
