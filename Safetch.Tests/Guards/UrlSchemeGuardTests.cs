using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Guards;
using Safetch.Core.Models;
using Xunit;

namespace Safetch.Tests.Guards;

public class UrlSchemeGuardTests
{
    private readonly UrlSchemeGuard _sut = new();

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    public async Task CheckAsync_ValidScheme_ReturnsAllow(string url)
    {
        var result = await _sut.CheckAsync(new FetchRequest { Url = url }, CancellationToken.None);
        Assert.True(result.Allowed);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public async Task CheckAsync_InvalidOrForbiddenScheme_ReturnsBlock(string url)
    {
        var result = await _sut.CheckAsync(new FetchRequest { Url = url }, CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }
}