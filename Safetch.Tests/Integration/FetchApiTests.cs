using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Safetch.Core.Models;
using Safetch.Core.Processing;
using Safetch.Tests.Integration.Fixtures;
using Xunit;

namespace Safetch.Tests.Integration;

public class FetchApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public FetchApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── POST /api/fetch — Request Validation ─────────────────────────────────

    [Fact] // 4.1
    public async Task Post_MissingUrl_Returns400WithError()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("url is required", body.GetProperty("error").GetString());
    }

    [Fact] // 4.2
    public async Task Post_EmptyUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch", new { url = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // 4.3
    public async Task Post_IdentityKeyTooLong_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com", identityKey = "123456789" }); // 9 chars

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("identityKey", body.GetProperty("error").GetString());
    }

    [Fact] // 4.4
    public async Task Post_IdentityKeyWithNonPrintableAscii_Returns400()
    {
        // \x01 is a non-printable control character
        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com", identityKey = "ab\u0001cd" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // 4.5
    public async Task Post_InvalidJson_Returns400()
    {
        var content = new StringContent("not-json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/fetch", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid JSON request body.", body.GetProperty("error").GetString());
    }

    [Fact] // 4.6
    public async Task Post_ValidRequest_Returns200WithCamelCaseJson()
    {
        _factory.FetchService.NextResponse = new FetchResponse
        {
            Success = true,
            Url = "https://example.com",
            Content = "hello world",
            StatusCode = 200,
            ContentType = "text/html",
            ContentBytes = 11,
            SpotlightingKey = "testkey1"
        };

        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://example.com", body.GetProperty("url").GetString());
        Assert.Equal("hello world", body.GetProperty("content").GetString());
        Assert.Equal(200, body.GetProperty("statusCode").GetInt32());
        Assert.Equal("text/html", body.GetProperty("contentType").GetString());
        Assert.Equal("testkey1", body.GetProperty("spotlightingKey").GetString());
    }

    [Fact] // 4.7
    public async Task Post_ModeMarkdown_PassesModeToService()
    {
        await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com", mode = "markdown" });

        Assert.Equal(ResponseMode.Markdown, _factory.FetchService.LastRequest?.Mode);
    }

    // ── GET /api/fetch — Request Validation ──────────────────────────────────

    [Fact] // 4.8
    public async Task Get_MissingUrl_Returns400()
    {
        var response = await _client.GetAsync("/api/fetch");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // 4.9
    public async Task Get_RelativeUrl_Returns400()
    {
        var response = await _client.GetAsync("/api/fetch?url=/relative/path");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("HTTP/HTTPS", body.GetProperty("error").GetString());
    }

    [Fact] // 4.10
    public async Task Get_FtpScheme_Returns400()
    {
        var response = await _client.GetAsync("/api/fetch?url=ftp://example.com/file");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // 4.11
    public async Task Get_ValidUrl_Returns200()
    {
        var response = await _client.GetAsync("/api/fetch?url=https://example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // 4.12
    public async Task Get_IdentityKeyTooLong_Returns400()
    {
        var response = await _client.GetAsync("/api/fetch?url=https://example.com&identityKey=123456789");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Error Code Propagation ────────────────────────────────────────────────

    [Fact] // 4.13
    public async Task Post_ServiceReturnsBlocked_Returns400()
    {
        _factory.FetchService.NextResponse = new FetchResponse
        {
            Success = false,
            ErrorCode = "BLOCKED",
            ErrorMessage = "URL is blocked"
        };

        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BLOCKED", body.GetProperty("errorCode").GetString());
    }

    [Fact] // 4.14
    public async Task Post_ServiceReturnsFetchFailed_Returns502()
    {
        _factory.FetchService.NextResponse = new FetchResponse
        {
            Success = false,
            ErrorCode = "FETCH_FAILED",
            ErrorMessage = "Network error"
        };

        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FETCH_FAILED", body.GetProperty("errorCode").GetString());
    }

    [Fact] // 4.15
    public async Task Post_ServiceThrowsException_Returns502WithFetchFailed()
    {
        _factory.FetchService.NextException = new InvalidOperationException("oops");

        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FETCH_FAILED", body.GetProperty("errorCode").GetString());
    }

    // ── Response Shape ────────────────────────────────────────────────────────

    [Fact] // 4.16
    public async Task Post_SuccessResponse_IsCamelCaseJson()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        var raw = await response.Content.ReadAsStringAsync();

        // camelCase fields must be present, not PascalCase
        Assert.Contains("\"url\"", raw);
        Assert.Contains("\"content\"", raw);
        Assert.Contains("\"statusCode\"", raw);
        Assert.Contains("\"contentType\"", raw);
        Assert.DoesNotContain("\"Url\"", raw);
        Assert.DoesNotContain("\"StatusCode\"", raw);
    }

    [Fact] // 4.17
    public async Task Post_ServiceReturnsInjectionWarnings_WarningsInResponse()
    {
        _factory.FetchService.NextResponse = new FetchResponse
        {
            Success = true,
            Url = "https://example.com",
            Content = "test",
            StatusCode = 200,
            InjectionWarnings = new[]
            {
                new InjectionWarning("PromptInjection", "ignore previous", InjectionSeverity.High)
            }
        };

        var response = await _client.PostAsJsonAsync("/api/fetch",
            new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var warnings = body.GetProperty("injectionWarnings");
        Assert.Equal(1, warnings.GetArrayLength());
    }
}
