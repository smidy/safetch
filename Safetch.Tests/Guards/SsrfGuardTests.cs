using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Safetch.Core.Guards;
using Safetch.Core.Models;
using Xunit;

namespace Safetch.Tests.Guards;

public class SsrfGuardTests
{
    private static SsrfGuard CreateSut(Func<string, CancellationToken, Task<IPAddress[]>> resolver)
        => new SsrfGuard(NullLogger<SsrfGuard>.Instance, resolver);

    [Fact]
    public async Task CheckAsync_ResolvesToPublicIp_ReturnsAllow()
    {
        var sut = CreateSut((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));
        var result = await sut.CheckAsync(new FetchRequest { Url = "http://example.com" }, CancellationToken.None);
        Assert.True(result.Allowed);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task CheckAsync_ResolvesToPrivateIp_ReturnsBlock(string privateIp)
    {
        var sut = CreateSut((_, _) => Task.FromResult(new[] { IPAddress.Parse(privateIp) }));
        var result = await sut.CheckAsync(new FetchRequest { Url = "http://internal.corp" }, CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task CheckAsync_DnsReturnsNoAddresses_ReturnsBlock()
    {
        var sut = CreateSut((_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var result = await sut.CheckAsync(new FetchRequest { Url = "http://nonexistent.example" }, CancellationToken.None);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task CheckAsync_DnsThrowsSocketException_ReturnsBlock()
    {
        var sut = CreateSut((_, _) => Task.FromException<IPAddress[]>(
            new System.Net.Sockets.SocketException()));
        var result = await sut.CheckAsync(new FetchRequest { Url = "http://nonexistent.example" }, CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task CheckAsync_AnyAddressPrivateInMultipleResults_ReturnsBlock()
    {
        // All addresses must be public — if any is private, block
        var sut = CreateSut((_, _) => Task.FromResult(new[]
        {
            IPAddress.Parse("93.184.216.34"),
            IPAddress.Parse("192.168.1.1")   // private — should trigger block
        }));
        var result = await sut.CheckAsync(new FetchRequest { Url = "http://example.com" }, CancellationToken.None);
        Assert.False(result.Allowed);
    }
}