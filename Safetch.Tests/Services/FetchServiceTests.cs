using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Safetch.Core.Guards;
using Safetch.Core.Http;
using Safetch.Core.Models;
using Safetch.Core.Processing;
using Safetch.Core.Services;
using Xunit;

namespace Safetch.Tests.Services;

public class FetchServiceTests
{
    // Builds a real SafeHttpFetcher with default options (used for guard-rejection tests — fetcher is never called)
    private static SafeHttpFetcher RealFetcher()
    {
        var opts = Options.Create(new FetchOptions());
        return new SafeHttpFetcher(opts, NullLogger<SafeHttpFetcher>.Instance);
    }

    // Builds a real pipeline with no processors (used for guard-rejection tests)
    private static ContentProcessorPipeline MockPipeline()
    {
        return new ContentProcessorPipeline(Enumerable.Empty<OrderedProcessor>());
    }

    private static FetchService CreateSut(IEnumerable<OrderedGuard> guards, SafeHttpFetcher? fetcher = null, ContentProcessorPipeline? pipeline = null)
        => new FetchService(guards, fetcher ?? RealFetcher(), NullLogger<FetchService>.Instance, pipeline ?? MockPipeline());

    // ── Guard pipeline ────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_GuardBlocks_ReturnsBlockedResponse()
    {
        var guard = new Mock<IRequestGuard>();
        guard.Setup(g => g.CheckAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(GuardResult.Block("test block"));
        guard.Setup(g => g.Name).Returns("TestGuard");

        var sut = CreateSut([new OrderedGuard(1, guard.Object)]);
        var result = await sut.FetchAsync(new FetchRequest { Url = "http://example.com" });

        Assert.False(result.Success);
        Assert.Equal("BLOCKED", result.ErrorCode);
        Assert.Equal("test block", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchAsync_GuardsRunInOrder()
    {
        var calls = new List<int>();

        var g1 = new Mock<IRequestGuard>();
        g1.Setup(g => g.Name).Returns("G1");
        g1.Setup(g => g.CheckAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
          .Callback(() => calls.Add(1))
          .ReturnsAsync(GuardResult.Allow());

        var g2 = new Mock<IRequestGuard>();
        g2.Setup(g => g.Name).Returns("G2");
        g2.Setup(g => g.CheckAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
          .Callback(() => calls.Add(2))
          .ReturnsAsync(GuardResult.Block("stop"));

        // Intentionally register in reverse order to verify sorting
        var sut = CreateSut([new OrderedGuard(2, g2.Object), new OrderedGuard(1, g1.Object)]);
        await sut.FetchAsync(new FetchRequest { Url = "http://example.com" });

        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public async Task FetchAsync_FirstGuardBlocks_SecondGuardNotCalled()
    {
        var g1 = new Mock<IRequestGuard>();
        g1.Setup(g => g.Name).Returns("G1");
        g1.Setup(g => g.CheckAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(GuardResult.Block("blocked by g1"));

        var g2 = new Mock<IRequestGuard>();
        g2.Setup(g => g.Name).Returns("G2");

        var sut = CreateSut([new OrderedGuard(1, g1.Object), new OrderedGuard(2, g2.Object)]);
        var result = await sut.FetchAsync(new FetchRequest { Url = "http://example.com" });

        Assert.False(result.Success);
        g2.Verify(g => g.CheckAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}