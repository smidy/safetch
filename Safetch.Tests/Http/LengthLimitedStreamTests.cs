using System.IO;
using System.Text;
using System.Threading.Tasks;
using Safetch.Core.Http;
using Xunit;

namespace Safetch.Tests.Http;

public class LengthLimitedStreamTests
{
    [Fact]
    public async Task ReadAsync_UnderLimit_ReadsAll()
    {
        var data = Encoding.UTF8.GetBytes("hello world");
        using var inner = new MemoryStream(data);
        using var sut = new LengthLimitedStream(inner, 100);
        using var reader = new StreamReader(sut);

        var result = await reader.ReadToEndAsync();
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ReadAsync_ExceedsLimit_ThrowsResponseTooLargeException()
    {
        var data = Encoding.UTF8.GetBytes(new string('x', 200));
        using var inner = new MemoryStream(data);
        using var sut = new LengthLimitedStream(inner, 100);
        using var reader = new StreamReader(sut, bufferSize: 64);

        await Assert.ThrowsAsync<ResponseTooLargeException>(() => reader.ReadToEndAsync());
    }

    [Fact]
    public void Read_ExceedsLimit_ThrowsResponseTooLargeException()
    {
        var data = Encoding.UTF8.GetBytes(new string('x', 200));
        using var inner = new MemoryStream(data);
        using var sut = new LengthLimitedStream(inner, 100);

        var buffer = new byte[200];
        Assert.Throws<ResponseTooLargeException>(() => sut.Read(buffer, 0, 200));
    }

    [Fact]
    public async Task ReadAsync_ExactlyAtLimit_DoesNotThrow()
    {
        var data = Encoding.UTF8.GetBytes(new string('x', 100));
        using var inner = new MemoryStream(data);
        using var sut = new LengthLimitedStream(inner, 100);
        using var reader = new StreamReader(sut, bufferSize: 64);

        // Should not throw — exactly at the limit
        var result = await reader.ReadToEndAsync();
        Assert.Equal(100, result.Length);
    }
}