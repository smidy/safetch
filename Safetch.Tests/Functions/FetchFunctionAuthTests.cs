using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using Safetch.Api.Functions;
using Safetch.Core.Auth;
using Safetch.Core.Models;
using Safetch.Core.Services;
using Safetch.Tests.Fakes;
using Xunit;

namespace Safetch.Tests.Functions;

public class FetchFunctionAuthTests
{
    private static readonly FakeHostEnvironment ProdEnv = new("Production");
    private static readonly FakeHostEnvironment DevEnv = new("Development");

    private static Mock<IApiKeyRateLimiter> PermissiveRateLimiter()
    {
        var mock = new Mock<IApiKeyRateLimiter>();
        mock.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(true, 1, 20, DateTimeOffset.UtcNow.AddHours(1)));
        return mock;
    }

    private static async Task<JsonElement> ReadBody(Microsoft.Azure.Functions.Worker.Http.HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new System.IO.StreamReader(response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public async Task PostFetch_NoAuthHeader_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        var mockStore = new Mock<IApiKeyStore>();
        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, PermissiveRateLimiter().Object, ProdEnv);

        var response = await function.Run(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHORIZED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task PostFetch_InvalidBearerToken_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("Authorization", "Bearer invalid-token");
        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("invalid-token", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string?)null);
        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, PermissiveRateLimiter().Object, ProdEnv);

        var response = await function.Run(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHORIZED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetFetch_NoAuthHeader_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        var mockStore = new Mock<IApiKeyStore>();
        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, PermissiveRateLimiter().Object, ProdEnv);

        var response = await function.RunGet(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHORIZED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetFetch_ValidBearerToken_CallsFetchService()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        req.Headers.Add("Authorization", "Bearer valid-token");

        var mockStore = new Mock<IApiKeyStore>();
        mockStore.Setup(x => x.ValidateKeyAsync("valid-token", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("github-user-123");

        var successResponse = new FetchResponse
        {
            Success = true,
            Url = "https://example.com",
            StatusCode = 200,
            ContentType = "text/html",
            Content = "hello"
        };

        var mockService = new Mock<IFetchService>();
        mockService.Setup(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(successResponse);

        var function = new FetchFunction(mockService.Object, mockStore.Object, PermissiveRateLimiter().Object, ProdEnv);

        var response = await function.RunGet(req, ctx);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mockService.Verify(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once());
    }

    // ── Development mode bypass tests ──────────────────────────────────────

    [Fact]
    public async Task PostFetch_DevelopmentMode_NoAuthHeader_BypassesAuth_Returns400ForEmptyBody()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx); // no auth header, empty body
        var mockStore = new Mock<IApiKeyStore>();
        var mockService = new Mock<IFetchService>();
        var function = new FetchFunction(mockService.Object, mockStore.Object, new Mock<IApiKeyRateLimiter>().Object, DevEnv);

        var response = await function.Run(req, ctx);

        // Auth bypassed — reaches body parsing, empty body → 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Critically: NOT a 401
        var body = await ReadBody(response);
        Assert.False(body.TryGetProperty("errorCode", out var ec) && ec.GetString() == "UNAUTHORIZED");
    }

    [Fact]
    public async Task GetFetch_DevelopmentMode_NoAuthHeader_BypassesAuth_CallsFetchService()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/fetch?url=https%3A%2F%2Fexample.com");
        // No Authorization header

        var mockStore = new Mock<IApiKeyStore>();
        var successResponse = new FetchResponse
        {
            Success = true,
            Url = "https://example.com",
            StatusCode = 200,
            ContentType = "text/html",
            Content = "hello"
        };
        var mockService = new Mock<IFetchService>();
        mockService.Setup(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(successResponse);

        var function = new FetchFunction(mockService.Object, mockStore.Object, new Mock<IApiKeyRateLimiter>().Object, DevEnv);

        var response = await function.RunGet(req, ctx);

        // Auth bypassed — fetch proceeds
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mockService.Verify(x => x.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once());
    }
}
