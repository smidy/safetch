using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Safetch.Core.Auth;
using Safetch.Core.Guards;
using Xunit;

namespace Safetch.Tests.Auth;

public class InMemoryRateLimiterTests
{
    private static InMemoryRateLimiter CreateLimiter(int maxPerWindow = 5, TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
    {
        var options = Options.Create(new RateLimitOptions
        {
            Limits = new()
            {
                new RateLimitTier { MaxFetchesPerWindow = maxPerWindow, Window = window ?? TimeSpan.FromHours(1) }
            }
        });
        return new InMemoryRateLimiter(options, clock);
    }

    [Fact]
    public async Task UnderLimit_ReturnsAllowed()
    {
        var limiter = CreateLimiter(maxPerWindow: 5);
        var result = await limiter.CheckAndIncrementAsync("user-a");
        Assert.True(result.Allowed);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task AtLimit_ReturnsNotAllowed()
    {
        var limiter = CreateLimiter(maxPerWindow: 5);
        for (int i = 0; i < 5; i++)
            await limiter.CheckAndIncrementAsync("user-a");

        var result = await limiter.CheckAndIncrementAsync("user-a");
        Assert.False(result.Allowed);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task AtLimit_TierLabelIsPopulated()
    {
        var limiter = CreateLimiter(maxPerWindow: 3, window: TimeSpan.FromMinutes(1));
        for (int i = 0; i < 3; i++)
            await limiter.CheckAndIncrementAsync("user-a");

        var result = await limiter.CheckAndIncrementAsync("user-a");
        Assert.False(result.Allowed);
        Assert.Equal("3 requests per minute", result.TierLabel);
    }

    [Fact]
    public async Task WindowReset_ResetsCounter()
    {
        var now = DateTimeOffset.UtcNow;
        // Start at top of the current hour
        var startTime = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var currentTime = startTime;
        var limiter = CreateLimiter(maxPerWindow: 3, clock: () => currentTime);

        // Hit the limit
        for (int i = 0; i < 3; i++)
            await limiter.CheckAndIncrementAsync("user-a");

        var blocked = await limiter.CheckAndIncrementAsync("user-a");
        Assert.False(blocked.Allowed);

        // Advance clock by 1 hour — new window
        currentTime = startTime.AddHours(1);

        var reset = await limiter.CheckAndIncrementAsync("user-a");
        Assert.True(reset.Allowed);
        Assert.Equal(1, reset.Count);
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotExceedLimit()
    {
        var limiter = CreateLimiter(maxPerWindow: 5);

        for (int i = 0; i < 5; i++)
        {
            var r = await limiter.CheckAndIncrementAsync("user-a");
            Assert.True(r.Allowed);
        }

        var over = await limiter.CheckAndIncrementAsync("user-a");
        Assert.False(over.Allowed);
        Assert.Equal(5, over.Count);
    }

    [Fact]
    public async Task DifferentUsers_HaveSeparateCounters()
    {
        var limiter = CreateLimiter(maxPerWindow: 5);

        // Hit limit for user-a
        for (int i = 0; i < 5; i++)
            await limiter.CheckAndIncrementAsync("user-a");

        // user-b should still be allowed
        var result = await limiter.CheckAndIncrementAsync("user-b");
        Assert.True(result.Allowed);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task NoLimitsConfigured_FallsBackToDefault_Allows()
    {
        var options = Options.Create(new RateLimitOptions { Limits = new() });
        var limiter = new InMemoryRateLimiter(options);
        var result = await limiter.CheckAndIncrementAsync("user-a");
        Assert.True(result.Allowed);
    }
}
