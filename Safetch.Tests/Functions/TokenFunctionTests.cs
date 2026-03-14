using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Safetch.Api.Functions;
using Safetch.Core.Auth;
using Safetch.Tests.Fakes;
using Xunit;

namespace Safetch.Tests.Functions;

public class TokenFunctionTests
{
    private readonly Mock<IApiKeyStore> _mockStore = new();
    // All tests run in Production mode so auth checks are enforced
    private static readonly FakeHostEnvironment ProdEnv = new("Production");
    private static readonly NullLogger<TokenFunction> NullLog = new();

    private static string BuildPrincipalHeader(string userId, string login)
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": [{ \"typ\": \"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier\", \"val\": \"" + userId + "\" }, { \"typ\": \"urn:github:login\", \"val\": \"" + login + "\" }]}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private static async Task<JsonElement> ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new System.IO.StreamReader(response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public async Task GetToken_NoAuthHeader_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.GetToken(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHENTICATED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetToken_InvalidPrincipalHeader_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("x-ms-client-principal", "invalid-base64!!!");
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.GetToken(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHENTICATED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetToken_NoKeyExists_Returns404()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("x-ms-client-principal", BuildPrincipalHeader("123", "octocat"));
        _mockStore.Setup(x => x.GetKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string?)null);
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.GetToken(req, ctx);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("NOT_FOUND", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetToken_KeyExists_ReturnsKey()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("x-ms-client-principal", BuildPrincipalHeader("123", "octocat"));
        _mockStore.Setup(x => x.GetKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("abc123");
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.GetToken(req, ctx);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("abc123", body.GetProperty("apiKey").GetString());
        Assert.Equal("octocat", body.GetProperty("githubLogin").GetString());
    }

    [Fact]
    public async Task PostToken_NoAuthHeader_Returns401()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.PostToken(req, ctx);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("UNAUTHENTICATED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task PostToken_ExistingKey_Returns200WithKey()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("x-ms-client-principal", BuildPrincipalHeader("123", "octocat"));
        _mockStore.Setup(x => x.GetKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("abc123");
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.PostToken(req, ctx);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("abc123", body.GetProperty("apiKey").GetString());
        Assert.Equal("octocat", body.GetProperty("githubLogin").GetString());
        Assert.False(body.GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task PostToken_NewKey_Returns201WithKey()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx);
        req.Headers.Add("x-ms-client-principal", BuildPrincipalHeader("123", "octocat"));
        _mockStore.Setup(x => x.GetKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockStore.Setup(x => x.CreateKeyAsync("123", "octocat", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("def456");
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.PostToken(req, ctx);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("def456", body.GetProperty("apiKey").GetString());
        Assert.Equal("octocat", body.GetProperty("githubLogin").GetString());
        Assert.True(body.GetProperty("created").GetBoolean());
    }

    // ── Development mode bypass tests ──────────────────────────────────────

    [Fact]
    public async Task GetToken_DevelopmentMode_NoHeader_UsesDevIdentity_Returns404WhenNoKey()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx); // no principal header
        var store = new Mock<IApiKeyStore>();
        store.Setup(x => x.GetKeyAsync("dev-user", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string?)null);
        var function = new TokenFunction(store.Object, new FakeHostEnvironment("Development"), NullLog);

        var response = await function.GetToken(req, ctx);

        // Should not be 401 — dev mode uses the fake identity
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostToken_DevelopmentMode_NoHeader_GeneratesKey()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx); // no principal header
        var store = new Mock<IApiKeyStore>();
        store.Setup(x => x.GetKeyAsync("dev-user", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((string?)null);
        store.Setup(x => x.CreateKeyAsync("dev-user", "local-dev", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("dev-generated-key");
        var function = new TokenFunction(store.Object, new FakeHostEnvironment("Development"), NullLog);

        var response = await function.PostToken(req, ctx);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("dev-generated-key", body.GetProperty("apiKey").GetString());
        Assert.Equal("local-dev", body.GetProperty("githubLogin").GetString());
    }
    [Fact]
    public async Task PostToken_RegenerateTrue_DeletesOldKeyAndReturnsNew()
    {
        var ctx = new FakeFunctionContext();
        var req = new FakeHttpRequestData(ctx, url: "http://localhost/api/token?regenerate=true");
        req.Headers.Add("x-ms-client-principal", BuildPrincipalHeader("123", "octocat"));
        _mockStore.Setup(x => x.GetKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("oldkey");
        _mockStore.Setup(x => x.DeleteKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockStore.Setup(x => x.CreateKeyAsync("123", "octocat", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("newkey");
        var function = new TokenFunction(_mockStore.Object, ProdEnv, NullLog);

        var response = await function.PostToken(req, ctx);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadBody(response);
        Assert.Equal("newkey", body.GetProperty("apiKey").GetString());
        Assert.Equal("octocat", body.GetProperty("githubLogin").GetString());
        Assert.True(body.GetProperty("created").GetBoolean());
        // Verify DeleteKeyAsync was called
        _mockStore.Verify(x => x.DeleteKeyAsync("123", It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

}
