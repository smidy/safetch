using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Safetch.Api.Functions;
using Safetch.Core.Models;
using Safetch.Core.Services;
using Safetch.Tests.Fakes;
using Xunit;

namespace Safetch.Tests.Functions;

public class FetchFunctionTests
{
    private static FetchFunction CreateSut(Mock<IFetchService>? mock = null)
    {
        mock ??= new Mock<IFetchService>();
        return new FetchFunction(mock.Object);
    }

    private static FakeHttpRequestData MakeRequest(string body)
        => new FakeHttpRequestData(new FakeFunctionContext(), body);

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
        // Helper to read body from response
        private static async Task<JsonElement> ReadJsonBody(HttpResponseData response)
        {
            response.Body.Position = 0;
            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        private static FakeHttpRequestData MakeRequest(string body)
            => new FakeHttpRequestData(new FakeFunctionContext(), body);

        [Fact]
        public async Task Run_InvalidJson_ErrorResponseIncludesErrorCode()
        {
            var sut = new FetchFunction(new Mock<IFetchService>().Object);
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

            var sut = new FetchFunction(mock.Object);
            var result = await sut.Run(MakeRequest($"{{\u0022url\u0022:\u0022http://example.com\u0022}}"), new FakeFunctionContext());
            var body = await ReadJsonBody(result);

            Assert.Equal("BLOCKED", body.GetProperty("errorCode").GetString());
            Assert.Equal("bad url", body.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Run_ValidRequestWithSessionId_ResponseIncludesSessionId()
    {
        var mock = new Mock<IFetchService>();
        mock.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), default))
            .ReturnsAsync(new FetchResponse { Success = true, Url = "http://example.com", Content = "hi", StatusCode = 200, SessionId = "sess-123" });

        var body = JsonSerializer.Serialize(new { url = "http://example.com", sessionId = "sess-123" });
        var result = await CreateSut(mock).Run(MakeRequest(body), new FakeFunctionContext());

        var json = await ReadBody(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("sess-123", doc.GetProperty("sessionId").GetString());
    }
}