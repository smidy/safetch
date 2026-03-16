using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Safetch.Core.Auth;
using Safetch.Core.Guards;
using Xunit;

namespace Safetch.Tests.Auth;

public class TableApiKeyRateLimiterTests
{
    private static IOptions<RateLimitOptions> SingleTierOptions(int max = 5, TimeSpan? window = null) =>
        Options.Create(new RateLimitOptions
        {
            Limits = new List<RateLimitTier>
            {
                new() { MaxFetchesPerWindow = max, Window = window ?? TimeSpan.FromHours(1) }
            }
        });

    [Fact]
    public async Task UnderLimit_ReturnsAllowed()
    {
        // Arrange
        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        mockTable.Setup(t => t.AddEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, SingleTierOptions(5), logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.True(result.Allowed);
        Assert.Equal(1, result.Count);
        Assert.Equal(5, result.Limit);
    }

    [Fact]
    public async Task AtLimit_ReturnsNotAllowed()
    {
        // Arrange
        var entity = new TableEntity("ratelimit", "test-api-key:36000000000:1717027200") { ["Count"] = 5 };
        entity.ETag = new ETag("etag-value");
        var mockResponse = new Mock<Response<TableEntity>>();
        mockResponse.SetupGet(r => r.Value).Returns(entity);

        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                "ratelimit", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, SingleTierOptions(5), logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.False(result.Allowed);
        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.Limit);
    }

    [Fact]
    public async Task AtLimit_TierLabelIsPopulated()
    {
        // Arrange
        var entity = new TableEntity("ratelimit", "test-api-key:600000000:1717027200") { ["Count"] = 10 };
        entity.ETag = new ETag("etag-value");
        var mockResponse = new Mock<Response<TableEntity>>();
        mockResponse.SetupGet(r => r.Value).Returns(entity);

        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                "ratelimit", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, SingleTierOptions(10, TimeSpan.FromMinutes(1)), logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.False(result.Allowed);
        Assert.Equal("10 requests per minute", result.TierLabel);
    }

    [Fact]
    public async Task RowKey_IncludesTierWindowTicks()
    {
        // Arrange — row key must include the tier's window ticks so different tiers don't collide
        var now = DateTimeOffset.UtcNow;
        var tierWindow = TimeSpan.FromHours(1);
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % tierWindow.Ticks),
            TimeSpan.Zero);
        var expectedWindowKey = windowStart.ToUnixTimeSeconds().ToString();
        var expectedTierTicks = tierWindow.Ticks.ToString();

        string? capturedRowKey = null;
        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        mockTable.Setup(t => t.AddEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TableEntity, CancellationToken>((e, _) => capturedRowKey = e.RowKey)
            .ReturnsAsync(Mock.Of<Response>());

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, SingleTierOptions(5, tierWindow), logger);

        // Act
        await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.NotNull(capturedRowKey);
        Assert.Contains(expectedWindowKey, capturedRowKey);
        Assert.Contains(expectedTierTicks, capturedRowKey);
        Assert.DoesNotContain("yyyyMMddHH", capturedRowKey);
    }

    [Fact]
    public async Task MultiTier_FirstTierExceeded_ReturnsNotAllowed_WithFirstTierLabel()
    {
        // Arrange: minute tier (count=10, limit=10) already at limit; hour tier not yet reached
        var minuteEntity = new TableEntity("ratelimit", "key-a:600000000:xxx") { ["Count"] = 10 };
        minuteEntity.ETag = new ETag("etag-1");
        var minuteResponse = new Mock<Response<TableEntity>>();
        minuteResponse.SetupGet(r => r.Value).Returns(minuteEntity);

        var mockTable = new Mock<TableClient>();
        // First GetEntity call (minute tier) returns count=10
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                "ratelimit", It.Is<string>(k => k.Contains(TimeSpan.FromMinutes(1).Ticks.ToString())), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(minuteResponse.Object);

        var options = Options.Create(new RateLimitOptions
        {
            Limits = new List<RateLimitTier>
            {
                new() { MaxFetchesPerWindow = 10, Window = TimeSpan.FromMinutes(1) },
                new() { MaxFetchesPerWindow = 100, Window = TimeSpan.FromHours(1) }
            }
        });

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, options, logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("key-a");

        // Assert: blocked on first (minute) tier
        Assert.False(result.Allowed);
        Assert.Equal("10 requests per minute", result.TierLabel);
    }

    [Fact]
    public async Task MultiTier_AllTiersPassed_ReturnsAllowed()
    {
        // Arrange: both tiers have no existing rows (404) → both allow
        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));
        mockTable.Setup(t => t.AddEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var options = Options.Create(new RateLimitOptions
        {
            Limits = new List<RateLimitTier>
            {
                new() { MaxFetchesPerWindow = 10, Window = TimeSpan.FromMinutes(1) },
                new() { MaxFetchesPerWindow = 100, Window = TimeSpan.FromHours(1) }
            }
        });

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, options, logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("key-b");

        // Assert
        Assert.True(result.Allowed);
        Assert.Null(result.TierLabel);
    }

    [Fact]
    public async Task WindowKey_ChangesWithConfiguredWindow()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % TimeSpan.FromHours(1).Ticks),
            TimeSpan.Zero);
        var expectedWindowKey = windowStart.ToUnixTimeSeconds().ToString();

        string? capturedRowKey = null;
        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        mockTable.Setup(t => t.AddEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TableEntity, CancellationToken>((entity, _) => capturedRowKey = entity.RowKey)
            .ReturnsAsync(Mock.Of<Response>());

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, SingleTierOptions(5), logger);

        // Act
        await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.NotNull(capturedRowKey);
        Assert.Contains(expectedWindowKey, capturedRowKey);
        Assert.DoesNotContain("yyyyMMddHH", capturedRowKey);
    }
}
