using System.Linq;
using System.Threading.Tasks;
using Safetch.Core.Processing;
using Xunit;

namespace Safetch.Tests.Processing;

public class UnicodeTagStripProcessorTests
{
    private readonly UnicodeTagStripProcessor _processor = new();

    [Fact]
    public async Task StripsUnicodeTagCharacters()
    {
        // \uDB40\uDC00 is U+E0000, first tag character
        var content = "Hello\uDB40\uDC00World";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Equal("HelloWorld", result.Content);
    }

    [Fact]
    public async Task InjectionWarningsEmptyAfterStripping()
    {
        // UnicodeTagStrip only strips characters — it does not produce injection warnings
        var content = "Hello\uDB40\uDC00World";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Empty(result.InjectionWarnings);
    }

    [Fact]
    public async Task PassesCleanContentUnchanged()
    {
        var content = "Hello World";
        var result = await _processor.ProcessAsync(content, new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Equal(content, result.Content);
        Assert.Empty(result.InjectionWarnings);
    }

    [Fact]
    public async Task HandlesEmptyStringWithoutThrowing()
    {
        var result = await _processor.ProcessAsync("", new ProcessingContext("text/plain", "http://example.com"), default);
        Assert.Equal("", result.Content);
        Assert.Empty(result.InjectionWarnings);
    }
}
