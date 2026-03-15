using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Safetch.Api.Functions;
using Safetch.Core.Auth;
using Safetch.Core.Models;
using Safetch.Core.Services;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;
using Safetch.Tests.Fakes;
using Xunit;

namespace Safetch.Tests.Functions;

public class FetchFunctionTests
{
    private const string ValidToken = "test-api-key";

    private static Mock<IApiKeyStore> AuthorizedStore()
    {
        var store = new Mock<IApiKeyStore>();
        store.Setup(s => s.ValidateKeyAsync(ValidToken, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("github-user-1");
        return store;
    }

    private static readonly FakeHostEnvironment ProdEnv = new("Production");

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
        return new FetchFunction(mock.Object, store.Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()));
    }

    // Creates a POST request with the valid bearer token already set
    private static FakeHttpRequestData MakeRequest(string body)
    {
        var req = new FakeHttpRequestData(new FakeFunctionContext(), body);
        req.Headers.Add("Authorization", $"Bearer {ValidToken}");
        return req;
    }

    private static async Task<string> ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Run_EmptyBody_Returns400()
    {
        var result = await CreateSut().Run(MakeRequest(""), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Run_InvalidJson_Returns400()
    {
        var result = await CreateSut().Run(MakeRequest("not json"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Run_MissingUrlField_Returns400()
    {
        var result = await CreateSut().Run(MakeRequest("{}"), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Run_ValidRequest_Returns200WithFetchResponse()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "http://example.com", Content = "hi", StatusCode = 200 });

        var body = JsonSerializer.Serialize(new { url = "http://example.com" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var json = await ReadBody(result);
        var fetched = JsonSerializer.Deserialize<FetchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(fetched);
        Assert.True(fetched!.Success);
        Assert.Equal("http://example.com", fetched.Url);
    }

    [Fact]
    public async Task Run_ServiceReturnsBlocked_Returns400()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = false, ErrorCode = "BLOCKED", ErrorMessage = "bad scheme" });

        var body = JsonSerializer.Serialize(new { url = "ftp://bad" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Run_ServiceReturnsFetchFailed_Returns502()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = false, ErrorCode = "FETCH_FAILED", ErrorMessage = "DNS failed" });

        var body = JsonSerializer.Serialize(new { url = "http://example.com" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task Run_ServiceThrowsUnexpected_Returns502()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ThrowsAsync(new System.Exception("boom"));

        var body = JsonSerializer.Serialize(new { url = "http://example.com" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    public class FetchFunctionErrorShapeTests
    {
        private const string ValidToken = "test-api-key";

        private static Mock<IApiKeyStore> AuthorizedStore()
        {
            var store = new Mock<IApiKeyStore>();
            store.Setup(s => s.ValidateKeyAsync(ValidToken, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("github-user-1");
            return store;
        }

        private static async Task<JsonElement> ReadJsonBody(HttpResponseData response)
        {
            response.Body.Position = 0;
            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        private static FakeHttpRequestData MakeRequest(string body)
        {
            var req = new FakeHttpRequestData(new FakeFunctionContext(), body);
            req.Headers.Add("Authorization", $"Bearer {ValidToken}");
            return req;
        }

        private static readonly FakeHostEnvironment ProdEnv = new("Production");

        [Fact]
        public async Task Run_InvalidJson_ErrorResponseIncludesErrorCode()
        {
            var sut = new FetchFunction(new Mock<IFetchService>().Object, AuthorizedStore().Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()));
            var result = await sut.Run(MakeRequest("not json"), new FakeFunctionContext());
            var body = await ReadJsonBody(result);
            Assert.True(body.TryGetProperty("error", out _));
            Assert.True(body.TryGetProperty("errorCode", out _));
        }

        [Fact]
        public async Task Run_BlockedByService_ErrorResponseIncludesErrorCode()
        {
            var mock = new Mock<IFetchService>();
            mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
                .ReturnsAsync(new FetchResponse { Success = false, ErrorCode = "BLOCKED", ErrorMessage = "bad url" });

            var sut = new FetchFunction(mock.Object, AuthorizedStore().Object, PermissiveRateLimiter().Object, ProdEnv, Options.Create(new RateLimitOptions()));
            var result = await sut.Run(MakeRequest($"{{\u0022url\u0022:\u0022http://example.com\u0022}}"), new FakeFunctionContext());
            var body = await ReadJsonBody(result);

            Assert.Equal("BLOCKED", body.GetProperty("errorCode").GetString());
            Assert.Equal("bad url", body.GetProperty("error").GetString());
        }
    }


    [Fact]
    public async Task Run_InvalidIdentityKey_TooLong_Returns400()
    {
        var body = JsonSerializer.Serialize(new { url = "http://example.com", identityKey = "toolongkey" });
        var result = await CreateSut().Run(MakeRequest(body), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var json = await ReadBody(result);
        Assert.Contains("identityKey", json);
    }

    [Fact]
    public async Task Run_InvalidIdentityKey_NonAscii_Returns400()
    {
        var body = JsonSerializer.Serialize(new { url = "http://example.com", identityKey = "k\u00e9y" });
        var result = await CreateSut().Run(MakeRequest(body), new FakeFunctionContext());
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Run_ValidIdentityKey_PassedToFetchService()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "http://example.com", Content = "hi", StatusCode = 200, SpotlightingKey = "a3f1c92b" });

        var body = JsonSerializer.Serialize(new { url = "http://example.com", identityKey = "a3f1c92b" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        mock.Verify(s => s.FetchAsync(
            It.Is<FetchRequest>(r => r.IdentityKey == "a3f1c92b"), default), Times.Once);
    }

    [Fact]
    public async Task Run_SuccessResponse_IncludesSpotlightingKey()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "http://example.com", Content = "hi", StatusCode = 200, SpotlightingKey = "a3f1c92b" });

        var body = JsonSerializer.Serialize(new { url = "http://example.com", identityKey = "a3f1c92b" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());

        var json = await ReadBody(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty("spotlightingKey", out var keyProp));
        Assert.Equal("a3f1c92b", keyProp.GetString());
    }

    [Fact]
    public async Task Run_SuccessResponse_SerialisesCamelCase()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse
            {
                Success = true,
                Url = "http://example.com",
                Content = "hello",
                StatusCode = 200,
            });

        var body = JsonSerializer.Serialize(new { url = "http://example.com" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());

        var json = await ReadBody(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        // camelCase keys must be present
        Assert.True(doc.TryGetProperty("success", out _));
        Assert.True(doc.TryGetProperty("content", out _));
        Assert.True(doc.TryGetProperty("statusCode", out _));

        // PascalCase keys must NOT be present
        Assert.False(doc.TryGetProperty("Success", out _));
        Assert.False(doc.TryGetProperty("Content", out _));
        Assert.False(doc.TryGetProperty("InjectionWarnings", out _));
    }
}
