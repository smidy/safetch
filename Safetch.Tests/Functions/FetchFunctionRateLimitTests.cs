using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Safetch.Api.Functions;
using Safetch.Core.Auth;
using Safetch.Core.Guards;
using Safetch.Core.Models;
using Safetch.Core.Services;
using Safetch.Tests.Fakes;
using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Safetch.Tests.Functions;

public class FetchFunctionRateLimitTests
{
    private static readonly FakeHostEnvironment ProdEnv = new("Production");
    private static readonly FakeHostEnvironment DevEnv = new("Development");

    private static async Task<JsonElement> ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new System.IO.StreamReader(response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static Mock<IApiKeyRateLimiter> RateLimiterThatReturns(bool allowed, int count = 1, int limit = 20, string? tierLabel = null)
    {
        var mock = new Mock<IApiKeyRateLimiter>();
        mock.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(allowed, count, limit, DateTimeOffset.UtcNow.AddHours(1), tierLabel));
        return mock;
    }

    [Fact]
    public async Task PostFetch_WhenRateLimited_Returns429WithRetryAfter()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(false, 21, 20, "20 requests per hour");

        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        var response = await function.Run(req, ctx);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out _));
        var body = await ReadBody(response);
        Assert.Equal("RATE_LIMITED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task PostFetch_WhenRateLimited_ResponseIncludesRateLimitedErrorCode()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(false, 21, 20, "20 requests per hour");

        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        var response = await function.Run(req, ctx);

        var body = await ReadBody(response);
        Assert.Equal("RATE_LIMITED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task PostFetch_WhenRateLimited_ErrorMessageIncludesTierLabel()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(false, 11, 10, "10 requests per minute");

        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        var response = await function.Run(req, ctx);

        var body = await ReadBody(response);
        var errorMsg = body.GetProperty("error").GetString();
        Assert.NotNull(errorMsg);
        Assert.Contains("10 requests per minute", errorMsg);
    }

    [Fact]
    public async Task GetFetch_WhenRateLimited_Returns429WithRetryAfter()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(false, 21, 20, "20 requests per hour");

        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        var response = await function.RunGet(req, ctx);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out _));
        var body = await ReadBody(response);
        Assert.Equal("RATE_LIMITED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetFetch_WhenRateLimited_ErrorMessageIncludesTierLabel()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(false, 101, 100, "100 requests per day");

        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        var response = await function.RunGet(req, ctx);

        var body = await ReadBody(response);
        var errorMsg = body.GetProperty("error").GetString();
        Assert.NotNull(errorMsg);
        Assert.Contains("100 requests per day", errorMsg);
    }

    [Fact]
    public async Task PostFetch_DevelopmentMode_CallsRateLimiterWithLocalDevIdentity()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, body: """{"url":"https://example.com"}""");
        // No Authorization header — auth is bypassed in Dev

        var mockStore = new Mock<IApiKeyStore>();
        var mockRateLimiter = new Mock<IApiKeyRateLimiter>();
        mockRateLimiter.Setup(r => r.CheckAndIncrementAsync("local-dev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(true, 1, 100, DateTimeOffset.UtcNow.AddHours(1)));

        var mockService = new Mock<IFetchService>();
        mockService.Setup(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "https://example.com", StatusCode = 200, Content = "" });

        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, DevEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        await function.Run(req, ctx);

        mockRateLimiter.Verify(r => r.CheckAndIncrementAsync("local-dev", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task GetFetch_DevelopmentMode_CallsRateLimiterWithLocalDevIdentity()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, method: "GET", url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        // No Authorization header — auth is bypassed in Dev

        var mockStore = new Mock<IApiKeyStore>();
        var mockRateLimiter = new Mock<IApiKeyRateLimiter>();
        mockRateLimiter.Setup(r => r.CheckAndIncrementAsync("local-dev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(true, 1, 100, DateTimeOffset.UtcNow.AddHours(1)));

        var mockService = new Mock<IFetchService>();
        mockService.Setup(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "https://example.com", StatusCode = 200, Content = "" });

        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, DevEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        await function.RunGet(req, ctx);

        mockRateLimiter.Verify(r => r.CheckAndIncrementAsync("local-dev", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task PostFetch_WhenAllowed_CallsFetchService()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, body: """{"url":"https://example.com"}""");
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var mockRateLimiter = RateLimiterThatReturns(true);

        var mockService = new Mock<IFetchService>();
        mockService.Setup(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResponse { Success = true });

        var function = new FetchFunction(mockService.Object, mockStore.Object, mockRateLimiter.Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

        await function.Run(req, ctx);

        mockService.Verify(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}
