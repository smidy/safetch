using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Guards;
using Safetch.Core.Models;
using Xunit;

namespace Safetch.Tests.Guards;

public class EncodedIpGuardTests
{
    private readonly EncodedIpGuard _sut = new();

    [Theory]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://169.254.169.254")]   // AWS metadata
    [InlineData("http://0.0.0.0")]
    public async Task CheckAsync_PrivateIpLiteral_ReturnsBlock(string url)
    {
        var result = await _sut.CheckAsync(new FetchRequest { Url = url }, CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData("http://1.1.1.1")]
    [InlineData("https://8.8.8.8")]
    [InlineData("http://93.184.216.34")]   // example.com
    public async Task CheckAsync_PublicIpLiteral_ReturnsAllow(string url)
    {
        var result = await _sut.CheckAsync(new FetchRequest { Url = url }, CancellationToken.None);
        Assert.True(result.Allowed);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://api.github.com/repos")]
    public async Task CheckAsync_HostnameName_ReturnsAllow(string url)
    {
        // Hostnames are not IP literals — SsrfGuard handles DNS resolution
        var result = await _sut.CheckAsync(new FetchRequest { Url = url }, CancellationToken.None);
        Assert.True(result.Allowed);
    }
}