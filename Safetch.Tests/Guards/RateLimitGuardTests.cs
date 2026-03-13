using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;
using Safetch.Core.Models;
using Xunit;

namespace Safetch.Tests.Guards;

public class RateLimitGuardTests : IDisposable
{
    private readonly IMemoryCache _cache;

    public RateLimitGuardTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public void Dispose() => _cache.Dispose();

    private RateLimitGuard CreateSut(int maxFetches = 3, TimeSpan? window = null)
    {
        var opts = Options.Create(new RateLimitOptions
        {
            MaxFetchesPerSession = maxFetches,
            Window = window ?? TimeSpan.FromHours(1)
        });
        return new RateLimitGuard(_cache, opts);
    }

    [Fact]
    public async Task CheckAsync_NullSessionId_AlwaysAllows()
    {
        var sut = CreateSut(maxFetches: 1);
        // Call many times — should never block
        for (var i = 0; i < 10; i++)
        {
            var result = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = null }, CancellationToken.None);
            Assert.True(result.Allowed);
        }
    }

    [Fact]
    public async Task CheckAsync_EmptySessionId_AlwaysAllows()
    {
        var sut = CreateSut(maxFetches: 1);
        for (var i = 0; i < 10; i++)
        {
            var result = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "" }, CancellationToken.None);
            Assert.True(result.Allowed);
        }
    }

    [Fact]
    public async Task CheckAsync_UnderLimit_Allows()
    {
        var sut = CreateSut(maxFetches: 3);
        for (var i = 0; i < 3; i++)
        {
            var result = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-1" }, CancellationToken.None);
            Assert.True(result.Allowed, $"Call {i + 1} should be allowed");
        }
    }

    [Fact]
    public async Task CheckAsync_OverLimit_Blocks()
    {
        var sut = CreateSut(maxFetches: 3);
        for (var i = 0; i < 3; i++)
            await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-2" }, CancellationToken.None);

        var blocked = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-2" }, CancellationToken.None);
        Assert.False(blocked.Allowed);
        Assert.NotNull(blocked.Reason);
        Assert.Contains("Rate limit exceeded", blocked.Reason);
    }

    [Fact]
    public async Task CheckAsync_SessionsAreIsolated()
    {
        var sut = CreateSut(maxFetches: 2);

        // Exhaust session A
        await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-a" }, CancellationToken.None);
        await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-a" }, CancellationToken.None);
        var blockedA = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-a" }, CancellationToken.None);
        Assert.False(blockedA.Allowed);

        // Session B should still be allowed
        var allowedB = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-b" }, CancellationToken.None);
        Assert.True(allowedB.Allowed);
    }

    [Fact]
    public async Task CheckAsync_ConcurrentRequests_DoNotRacePastLimit()
    {
        var sut = CreateSut(maxFetches: 5);
        var request = new FetchRequest { Url = "http://example.com", SessionId = "sess-concurrent" };

        // Fire 20 concurrent requests
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => sut.CheckAsync(request, CancellationToken.None).AsTask())
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var allowed = results.Count(r => r.Allowed);
        var blocked = results.Count(r => !r.Allowed);

        // Exactly 5 should be allowed, 15 blocked
        Assert.Equal(5, allowed);
        Assert.Equal(15, blocked);
    }

    [Fact]
    public async Task CheckAsync_CounterResetsAfterWindowExpiry()
    {
        var sut = CreateSut(maxFetches: 2, window: TimeSpan.FromMilliseconds(100));

        // Exhaust the limit
        await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-exp" }, CancellationToken.None);
        await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-exp" }, CancellationToken.None);
        var blocked = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-exp" }, CancellationToken.None);
        Assert.False(blocked.Allowed);

        // Wait for the window to expire
        await Task.Delay(300);

        // Should be allowed again
        var allowed = await sut.CheckAsync(new FetchRequest { Url = "http://example.com", SessionId = "sess-exp" }, CancellationToken.None);
        Assert.True(allowed.Allowed);
    }
}