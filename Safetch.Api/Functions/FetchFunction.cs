using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Safetch.Core.Auth;
using Safetch.Core.Models;
using Safetch.Core.Services;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;

namespace Safetch.Api.Functions;

public class FetchFunction
{
    private readonly IFetchService _fetchService;
    private readonly IApiKeyStore _apiKeyStore;
    private readonly IApiKeyRateLimiter _rateLimiter;
    private readonly bool _isDevelopment;
    private readonly RateLimitOptions _rateLimitOptions;

    public FetchFunction(IFetchService fetchService, IApiKeyStore apiKeyStore, IApiKeyRateLimiter rateLimiter, IHostEnvironment environment, IOptions<RateLimitOptions> rateLimitOptions)
    {
        _fetchService = fetchService ?? throw new ArgumentNullException(nameof(fetchService));
        _apiKeyStore = apiKeyStore ?? throw new ArgumentNullException(nameof(apiKeyStore));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _isDevelopment = environment?.IsDevelopment() ?? false;
        _rateLimitOptions = rateLimitOptions.Value;
    }

    [Function("Fetch")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "fetch")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var response = req.CreateResponse();

        // Auth: bypassed in Development
        string? githubUserId = null;
        if (!_isDevelopment)
        {
            var token = ExtractBearerToken(req);
            githubUserId = token == null ? null : await _apiKeyStore.ValidateKeyAsync(token, executionContext.CancellationToken);
            if (githubUserId == null)
            {
                await WriteJsonResponseAsync(response, HttpStatusCode.Unauthorized, new { error = "A valid API key is required. Obtain one at /api/token.", errorCode = "UNAUTHORIZED" });
                return response;
            }
        }

        // Rate limiting: always enforced — in Development, use fixed local identity
        var identityForRateLimit = githubUserId ?? "local-dev";
        var rateLimit = await _rateLimiter.CheckAndIncrementAsync(identityForRateLimit, executionContext.CancellationToken);
        if (!rateLimit.Allowed)
        {
            var retryAfter = (int)Math.Ceiling((rateLimit.WindowResetsAt - DateTimeOffset.UtcNow).TotalSeconds);
            response.Headers.Add("Retry-After", retryAfter.ToString());
            await WriteJsonResponseAsync(response, HttpStatusCode.TooManyRequests,
                new { error = $"Rate limit exceeded. Maximum {_rateLimitOptions.MaxFetchesPerWindow} requests per hour.", errorCode = "RATE_LIMITED" });
            return response;
        }

        // Deserialize request body
        FetchRequest? fetchRequest;
        try
        {
            var dto = await JsonSerializer.DeserializeAsync<FetchRequestDto>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            fetchRequest = new FetchRequest
            {
                Url = dto?.Url,
                Mode = ParseMode(dto?.Mode)
            };
        }
        catch (JsonException)
        {
            await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest, new { error = "Invalid JSON request body.", errorCode = (string?)null });
            return response;
        }

        // Validate required URL field is present
        if (fetchRequest == null || string.IsNullOrWhiteSpace(fetchRequest.Url))
        {
            await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest, new { error = "url is required", errorCode = (string?)null });
            return response;
        }

        // FetchService never throws for URL/guard failures — it returns Success=false
        FetchResponse fetchResponse;
        try
        {
            fetchResponse = await _fetchService.FetchAsync(fetchRequest, executionContext.CancellationToken);
        }
        catch (Exception)
        {
            await WriteJsonResponseAsync(response, HttpStatusCode.BadGateway, new { error = "Failed to fetch the requested URL", errorCode = (string?)"FETCH_FAILED" });
            return response;
        }

        if (!fetchResponse.Success)
        {
            // BLOCKED = 400 Bad Request (caller sent a bad URL)
            // FETCH_FAILED = 502 Bad Gateway (network / remote server issue)
            var statusCode = fetchResponse.ErrorCode == "BLOCKED"
                ? HttpStatusCode.BadRequest
                : HttpStatusCode.BadGateway;
            await WriteJsonResponseAsync(response, statusCode, new { error = fetchResponse.ErrorMessage, errorCode = fetchResponse.ErrorCode });
            return response;
        }

        await WriteJsonResponseAsync(response, HttpStatusCode.OK, fetchResponse);
        return response;
    }

    private record FetchRequestDto(string? Url, string? Mode);

    private static ResponseMode ParseMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "readable" => ResponseMode.Readable,
        "text"     => ResponseMode.Text,
        "markdown" => ResponseMode.Markdown,
        _          => ResponseMode.Raw
    };

    [Function("FetchGet")]
    public async Task<HttpResponseData> RunGet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fetch")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var response = req.CreateResponse();

        // Auth: bypassed in Development
        string? githubUserId = null;
        if (!_isDevelopment)
        {
            var token = ExtractBearerToken(req);
            githubUserId = token == null ? null : await _apiKeyStore.ValidateKeyAsync(token, executionContext.CancellationToken);
            if (githubUserId == null)
            {
                await WriteJsonResponseAsync(response, HttpStatusCode.Unauthorized, new { error = "A valid API key is required. Obtain one at /api/token.", errorCode = "UNAUTHORIZED" });
                return response;
            }
        }

        // Rate limiting: always enforced — in Development, use fixed local identity
        var identityForRateLimit = githubUserId ?? "local-dev";
        var rateLimit = await _rateLimiter.CheckAndIncrementAsync(identityForRateLimit, executionContext.CancellationToken);
        if (!rateLimit.Allowed)
        {
            var retryAfter = (int)Math.Ceiling((rateLimit.WindowResetsAt - DateTimeOffset.UtcNow).TotalSeconds);
            response.Headers.Add("Retry-After", retryAfter.ToString());
            await WriteJsonResponseAsync(response, HttpStatusCode.TooManyRequests,
                new { error = $"Rate limit exceeded. Maximum {_rateLimitOptions.MaxFetchesPerWindow} requests per hour.", errorCode = "RATE_LIMITED" });
            return response;
        }

        // Parse query parameters manually — System.Web is not reliably available in isolated worker
        var query = req.Url.Query.TrimStart('?');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            var key = Uri.UnescapeDataString(part[..idx]);
            var val = Uri.UnescapeDataString(part[(idx + 1)..]);
            parameters[key] = val;
        }

        parameters.TryGetValue("url", out var url);
        parameters.TryGetValue("mode", out var modeStr);

        // Validate: url must be present, non-empty, and a valid absolute HTTP/HTTPS URI
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest,
                new { error = "url is required and must be a valid absolute HTTP/HTTPS URL", errorCode = (string?)null });
            return response;
        }

        var fetchRequest = new FetchRequest
        {
            Url = url,
            Mode = ParseMode(modeStr)
        };

        FetchResponse fetchResponse;
        try
        {
            fetchResponse = await _fetchService.FetchAsync(fetchRequest, executionContext.CancellationToken);
        }
        catch (Exception)
        {
            await WriteJsonResponseAsync(response, HttpStatusCode.BadGateway,
                new { error = "Failed to fetch the requested URL", errorCode = (string?)"FETCH_FAILED" });
            return response;
        }

        if (!fetchResponse.Success)
        {
            var statusCode = fetchResponse.ErrorCode == "BLOCKED"
                ? HttpStatusCode.BadRequest
                : HttpStatusCode.BadGateway;
            await WriteJsonResponseAsync(response, statusCode, new { error = fetchResponse.ErrorMessage, errorCode = fetchResponse.ErrorCode });
            return response;
        }

        await WriteJsonResponseAsync(response, HttpStatusCode.OK, fetchResponse);
        return response;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task WriteJsonResponseAsync<T>(HttpResponseData response, HttpStatusCode statusCode, T body)
    {
        response.StatusCode = statusCode;
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
    }

    private static string? ExtractBearerToken(HttpRequestData req)
    {
        req.Headers.TryGetValues("Authorization", out var values);
        var header = values != null ? string.Join("", values) : null;
        if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return header["Bearer ".Length..].Trim();
    }
}