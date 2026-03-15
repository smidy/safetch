using System.Threading.Tasks;
using Safetch.Core.Processing;
using Xunit;

namespace Safetch.Tests.Processing;

public class SpotlightingProcessorTests
{
    private readonly SpotlightingProcessor _processor = new();

    [Fact]
    public async Task EmbedsSameKeyInBothMarkers()
    {
        var result = await _processor.ProcessAsync("content", new ProcessingContext("text/plain", "http://example.com", "abc12345"), default);
        Assert.Contains("[BEGIN UNTRUSTED EXTERNAL CONTENT:abc12345", result.Content);
        Assert.Contains("[END UNTRUSTED EXTERNAL CONTENT:abc12345]", result.Content);
    }

    [Fact]
    public async Task AutoGeneratesKeyWhenNoneProvided()
    {
        var result = await _processor.ProcessAsync("content", new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Contains("[BEGIN UNTRUSTED EXTERNAL CONTENT:", result.Content);
        Assert.Contains("[END UNTRUSTED EXTERNAL CONTENT:", result.Content);
        // Both markers must contain the same auto-generated key
        var beginIdx = result.Content.IndexOf("[BEGIN UNTRUSTED EXTERNAL CONTENT:") + "[BEGIN UNTRUSTED EXTERNAL CONTENT:".Length;
        var endIdx = result.Content.IndexOf("[END UNTRUSTED EXTERNAL CONTENT:") + "[END UNTRUSTED EXTERNAL CONTENT:".Length;
        var beginKey = result.Content.Substring(beginIdx, 8);
        var endKey = result.Content.Substring(endIdx, 8);
        Assert.Equal(beginKey, endKey);
    }

    [Fact]
    public async Task OutputStartsWithBeginMarker()
    {
        var content = "Some content";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Contains("[BEGIN UNTRUSTED EXTERNAL CONTENT:", result.Content);
    }

    [Fact]
    public async Task OutputEndsWithEndMarker()
    {
        var content = "Some content";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Contains("[END UNTRUSTED EXTERNAL CONTENT:", result.Content);
    }

    [Fact]
    public async Task OriginalContentPreservedInsideMarkers()
    {
        var content = "Some content";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Contains("\n\nSome content\n\n", result.Content);
    }

    [Fact]
    public async Task HandlesEmptyString()
    {
        var result = await _processor.ProcessAsync("", new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Contains("[BEGIN UNTRUSTED EXTERNAL CONTENT:", result.Content);
        Assert.Contains("[END UNTRUSTED EXTERNAL CONTENT:", result.Content);
        Assert.Contains("\n\n\n\n", result.Content); // empty content between newlines
    }
}