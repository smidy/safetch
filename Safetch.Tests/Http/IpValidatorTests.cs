using System.Net;
using Safetch.Core.Http;
using Xunit;

namespace Safetch.Tests.Http;

public class IpValidatorTests
{
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]   // AWS metadata
    [InlineData("0.0.0.1")]
    [InlineData("::1")]               // IPv6 loopback
    [InlineData("fc00::1")]           // IPv6 ULA
    [InlineData("fd12:3456:789a::1")] // IPv6 ULA (fd prefix)
    [InlineData("fe80::1")]           // IPv6 link-local
    public void IsPrivate_PrivateAddress_ReturnsTrue(string ip)
    {
        Assert.True(IpValidator.IsPrivate(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]    // just outside 172.16/12
    [InlineData("172.32.0.0")]        // just outside 172.16/12
    [InlineData("11.0.0.1")]
    [InlineData("2606:4700:4700::1111")] // Cloudflare public IPv6
    public void IsPrivate_PublicAddress_ReturnsFalse(string ip)
    {
        Assert.False(IpValidator.IsPrivate(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivate_Ipv4MappedToIpv6_PrivateRange_ReturnsTrue()
    {
        // ::ffff:192.168.1.1 is IPv4-mapped IPv6 for 192.168.1.1
        var mapped = IPAddress.Parse("::ffff:192.168.1.1");
        Assert.True(IpValidator.IsPrivate(mapped));
    }

    [Fact]
    public void IsPrivate_Ipv4MappedToIpv6_PublicRange_ReturnsFalse()
    {
        var mapped = IPAddress.Parse("::ffff:1.1.1.1");
        Assert.False(IpValidator.IsPrivate(mapped));
    }
}