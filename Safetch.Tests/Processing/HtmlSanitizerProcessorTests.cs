using System.Threading.Tasks;
using Safetch.Core.Processing;
using Xunit;

namespace Safetch.Tests.Processing;

public class HtmlSanitizerProcessorTests
{
    private readonly HtmlSanitizerProcessor _processor = new();

    [Fact]
    public async Task RemovesOpacity0Element()
    {
        var html = @"<div style=""opacity:0"">hidden</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesOpacityWithSpaceElement()
    {
        var html = @"<div style=""opacity: 0"">hidden</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesDisplayNoneElement()
    {
        var html = @"<div style=""display:none"">hidden</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesVisibilityHiddenElement()
    {
        var html = @"<div style=""visibility:hidden"">hidden</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesDataStarAttributes()
    {
        var html = @"<div data-foo=""bar"" class=""keep"">content</div>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("data-foo", result.Content);
        Assert.Contains("class=\"keep\"", result.Content);
        Assert.Contains("content", result.Content);
    }

    [Fact]
    public async Task RemovesSvgBlock()
    {
        var html = @"<svg><rect width=""100"" height=""100""/></svg><p>text</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("<svg>", result.Content);
        Assert.Contains("<p>text</p>", result.Content);
    }

    [Fact]
    public async Task RemovesMetaHttpEquiv()
    {
        var html = @"<meta http-equiv=""refresh"" content=""5""><p>text</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("meta", result.Content);
        Assert.Contains("<p>text</p>", result.Content);
    }

    [Fact]
    public async Task PassesVisibleContentUnchanged()
    {
        var html = @"<p>Hello</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.Equal(html, result.Content);
    }

    [Fact]
    public async Task RemovesScriptElements()
    {
        var html = @"<script>alert('xss')</script><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("<script>", result.Content);
        Assert.DoesNotContain("alert", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesStyleElements()
    {
        var html = @"<style>body { display:none }</style><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("<style>", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesOnClickAttribute()
    {
        var html = @"<button onclick=""alert('xss')"">Click</button>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("onclick", result.Content);
        Assert.Contains("Click", result.Content);
    }

    [Fact]
    public async Task RemovesOnErrorAttribute()
    {
        var html = @"<img src=""x"" onerror=""fetch('https://evil.com')""/>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("onerror", result.Content);
    }

    [Fact]
    public async Task RemovesAllOnStarAttributes()
    {
        var html = @"<div onmouseover=""a()"" onload=""b()"" onfocus=""c()"">text</div>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("onmouseover", result.Content);
        Assert.DoesNotContain("onload", result.Content);
        Assert.DoesNotContain("onfocus", result.Content);
        Assert.Contains("text", result.Content);
    }

    [Fact]
    public async Task RemovesRgbWhiteColorElement()
    {
        var html = @"<span style=""color: rgb(255, 255, 255)"">hidden injection</span><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesRgbaZeroAlphaElement()
    {
        var html = @"<span style=""color: rgba(255, 0, 0, 0)"">hidden injection</span><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesOffScreenPositionedElement()
    {
        var html = @"<div style=""position: absolute; left: -9999px"">hidden injection</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesOffScreenTopPositionedElement()
    {
        var html = @"<div style=""position: fixed; top: -999px"">hidden injection</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task PreservesWhiteBackgroundColorElement()
    {
        // background-color:white is legitimate — must NOT be stripped
        var html = @"<div style=""background-color: rgb(255, 255, 255)"">visible content</div>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.Contains("visible content", result.Content);
    }

    [Fact]
    public async Task RemovesRgbLevel4WhiteColorElement()
    {
        // CSS Color Level 4 space-separated syntax: rgb(255 255 255)
        var html = @"<span style=""color: rgb(255 255 255)"">hidden injection</span><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesOffScreenEmPositionedElement()
    {
        var html = @"<div style=""position: absolute; left: -9999em"">hidden injection</div><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }

    [Fact]
    public async Task RemovesRgbaLevel4SlashSyntaxZeroAlphaElement()
    {
        // CSS Color Level 4 space-separated with slash: rgba(R G B / A)
        // e.g. rgba(255 0 0 / 0) is fully transparent — an active EchoLeak bypass vector
        var html = @"<span style=""color: rgba(255 0 0 / 0)"">hidden injection</span><p>visible</p>";
        var result = await _processor.ProcessAsync(html, new ProcessingContext("text/html", "http://example.com"), default);
        Assert.DoesNotContain("hidden injection", result.Content);
        Assert.Contains("<p>visible</p>", result.Content);
    }
}