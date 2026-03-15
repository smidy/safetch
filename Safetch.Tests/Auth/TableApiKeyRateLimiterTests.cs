using System;
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

        var options = Options.Create(new RateLimitOptions
        {
            MaxFetchesPerWindow = 5,
            Window = TimeSpan.FromHours(1)
        });

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, options, logger);

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
        var entity = new TableEntity("ratelimit", "test-api-key:1717027200") { ["Count"] = 5 };
        entity.ETag = new ETag("etag-value");
        var mockResponse = new Mock<Response<TableEntity>>();
        mockResponse.SetupGet(r => r.Value).Returns(entity);

        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                "ratelimit", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = Options.Create(new RateLimitOptions
        {
            MaxFetchesPerWindow = 5,
            Window = TimeSpan.FromHours(1)
        });

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, options, logger);

        // Act
        var result = await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.False(result.Allowed);
        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.Limit);
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

        string capturedRowKey = null;
        var mockTable = new Mock<TableClient>();
        mockTable.Setup(t => t.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        mockTable.Setup(t => t.AddEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TableEntity, CancellationToken>((entity, ct) =>
            {
                capturedRowKey = entity.RowKey;
            })
            .ReturnsAsync(Mock.Of<Response>());

        var options = Options.Create(new RateLimitOptions
        {
            MaxFetchesPerWindow = 5,
            Window = TimeSpan.FromHours(1)
        });

        var logger = Mock.Of<ILogger<TableApiKeyRateLimiter>>();
        var limiter = new TableApiKeyRateLimiter(mockTable.Object, options, logger);

        // Act
        await limiter.CheckAndIncrementAsync("test-api-key");

        // Assert
        Assert.NotNull(capturedRowKey);
        Assert.Contains(expectedWindowKey, capturedRowKey);
        Assert.DoesNotContain("yyyyMMddHH", capturedRowKey);
    }
}
