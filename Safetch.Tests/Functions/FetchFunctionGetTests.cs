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
using System.Net;
using System.Text.Json;

namespace Safetch.Tests.Functions;

public class FetchFunctionGetTests
{
    private const string ValidToken = "test-api-key";

    // Store mock that accepts any key and returns a valid user ID
    private static readonly FakeHostEnvironment ProdEnv = new("Production");

    private static Mock<IApiKeyStore> AuthorizedStore()
    {
        var store = new Mock<IApiKeyStore>();
        store.Setup(s => s.ValidateKeyAsync(ValidToken, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("github-user-1");
        return store;
    }

    private static Mock<IApiKeyRateLimiter> PermissiveRateLimiter()
    {
        var mock = new Mock<IApiKeyRateLimiter>();
        mock.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(true, 1, 20, DateTimeOffset.UtcNow.AddHours(1)));
        return mock;
    }

    private static FetchFunction CreateSut(Mock<IFetchService>? mock = null, Mock<IApiKeyStore>? store = null)
    {
        mock ??= new Mock<IFetchService>();
        store ??= AuthorizedStore();
        return new FetchFunction(mock.Object, store.Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());
    }

    private static FakeHttpRequestData MakeGetRequest(string queryString = "")
    {
        var req = new FakeHttpRequestData(
            new FakeFunctionContext(),
            body: "",
            method: "GET",
            url: $"http://localhost/api/fetch{(queryString.Length > 0 ? "?" + queryString : "")}");
        req.Headers.Add("Authorization", $"Bearer {ValidToken}");
        return req;
    }

    private static async Task<string> ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        return await reader.ReadToEndAsync();
    }

    // ── Integration-style tests ─────────────────────────────────────────────

    [Fact]
    public async Task RunGet_ValidUrl_Returns200()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "https://example.com", Content = "hi", StatusCode = 200 });

        var result = await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com"), new FakeFunctionContext());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var json = await ReadBody(result);
        var fetched = JsonSerializer.Deserialize<FetchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(fetched);
        Assert.True(fetched!.Success);
    }

    [Fact]
    public async Task RunGet_MissingUrlParam_Returns400()
    {
        var result = await CreateSut().RunGet(MakeGetRequest("someOtherParam=abc"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_EmptyUrlParam_Returns400()
    {
        var result = await CreateSut().RunGet(MakeGetRequest("url="), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_InvalidUrl_Returns400()
    {
        var result = await CreateSut().RunGet(MakeGetRequest("url=not-a-url"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_NonHttpUrl_Returns400()
    {
        var result = await CreateSut().RunGet(MakeGetRequest("url=ftp://bad"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_BlockedUrl_Returns400()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = false, ErrorCode = "BLOCKED", ErrorMessage = "blocked by guard" });

        var result = await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_FetchFailed_Returns502()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = false, ErrorCode = "FETCH_FAILED", ErrorMessage = "DNS failed" });

        var result = await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_ServiceThrows_Returns502()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ThrowsAsync(new System.Exception("boom"));

        var result = await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task RunGet_InvalidIdentityKey_TooLong_Returns400()
    {
        var result = await CreateSut().RunGet(MakeGetRequest("url=https://example.com&identityKey=toolongkey"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var json = await ReadBody(result);
        Assert.Contains("identityKey", json);
    }

    [Fact]
    public async Task RunGet_ValidIdentityKey_PassedToFetchService()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "https://example.com", Content = "hi", StatusCode = 200, SpotlightingKey = "a3f1c92b" });

        var result = await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com&identityKey=a3f1c92b"), new FakeFunctionContext());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        mock.Verify(s => s.FetchAsync(
            It.Is<FetchRequest>(r => r.IdentityKey == "a3f1c92b"), default), Times.Once);
    }
}

public class FetchFunctionGetParsingTests
{
    private const string ValidToken = "test-api-key";
    private static readonly FakeHostEnvironment ProdEnv = new("Production");

    private static Mock<IApiKeyStore> AuthorizedStore()
    {
        var store = new Mock<IApiKeyStore>();
        store.Setup(s => s.ValidateKeyAsync(ValidToken, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("github-user-1");
        return store;
    }

    private static Mock<IApiKeyRateLimiter> PermissiveRateLimiter()
    {
        var mock = new Mock<IApiKeyRateLimiter>();
        mock.Setup(r => r.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(true, 1, 20, DateTimeOffset.UtcNow.AddHours(1)));
        return mock;
    }

    private static FetchFunction CreateSut(Mock<IFetchService> mock, Mock<IApiKeyStore>? store = null)
        => new FetchFunction(mock.Object, (store ?? AuthorizedStore()).Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());

    private static FakeHttpRequestData MakeGetRequest(string queryString = "")
    {
        var req = new FakeHttpRequestData(
            new FakeFunctionContext(),
            body: "",
            method: "GET",
            url: $"http://localhost/api/fetch{(queryString.Length > 0 ? "?" + queryString : "")}");
        req.Headers.Add("Authorization", $"Bearer {ValidToken}");
        return req;
    }


    [Fact]
    public async Task RunGet_MissingUrl_DoesNotCallFetchAsync()
    {
        var mock = new Mock<IFetchService>();

        await CreateSut(mock).RunGet(MakeGetRequest("someOtherParam=abc"), new FakeFunctionContext());

        mock.Verify(s => s.FetchAsync(It.IsAny<FetchRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task RunGet_EmptyUrl_DoesNotCallFetchAsync()
    {
        var mock = new Mock<IFetchService>();

        await CreateSut(mock).RunGet(MakeGetRequest("url="), new FakeFunctionContext());

        mock.Verify(s => s.FetchAsync(It.IsAny<FetchRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task RunGet_InvalidUrl_DoesNotCallFetchAsync()
    {
        var mock = new Mock<IFetchService>();

        await CreateSut(mock).RunGet(MakeGetRequest("url=not-a-url"), new FakeFunctionContext());

        mock.Verify(s => s.FetchAsync(It.IsAny<FetchRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task RunGet_ModeMarkdown_PassesMarkdownModeToService()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true });

        await CreateSut(mock).RunGet(MakeGetRequest("url=https://example.com&mode=markdown"), new FakeFunctionContext());

        mock.Verify(s => s.FetchAsync(
            It.Is<FetchRequest>(r => r.Mode == ResponseMode.Markdown),
            default), Times.Once);
    }

    [Fact]
    public async Task RunPost_ModeMarkdown_PassesMarkdownModeToService()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true });

        var sut = new FetchFunction(mock.Object, AuthorizedStore().Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()), Mock.Of<ILogger<FetchFunction>>());
        var req = new FakeHttpRequestData(
            new FakeFunctionContext(),
            body: """{"url":"https://example.com","mode":"markdown"}""",
            method: "POST",
            url: "http://localhost/api/fetch");
        req.Headers.Add("Authorization", $"Bearer {ValidToken}");

        await sut.Run(req, new FakeFunctionContext());

        mock.Verify(s => s.FetchAsync(
            It.Is<FetchRequest>(r => r.Mode == ResponseMode.Markdown),
            default), Times.Once);
    }
}
