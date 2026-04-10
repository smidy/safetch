using Microsoft.Extensions.Options;
using Safetch.Core.Auth;
using Safetch.Core.Extensions;
using Safetch.Core.Guards;
using Safetch.Core.Http;
using Safetch.Core.Models;
using Safetch.Core.Processing;
using Safetch.Core.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Guards
builder.Services.AddRequestGuard<UrlSchemeGuard>(order: 1);
builder.Services.AddRequestGuard<EncodedIpGuard>(order: 2);
builder.Services.AddRequestGuard<SsrfGuard>(order: 3);

// Content processors
builder.Services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+readable", order: 1);
builder.Services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+text", order: 1);
builder.Services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+markdown", order: 1);
builder.Services.AddContentProcessor<HtmlSanitizerProcessor>(contentType: "text/html+markdown", order: 2);
builder.Services.AddContentProcessor<HtmlToMarkdownProcessor>(contentType: "text/html+markdown", order: 3);
builder.Services.AddContentProcessor<HtmlSanitizerProcessor>(contentType: "text/html", order: 2);
builder.Services.AddContentProcessor<HtmlToMarkdownProcessor>(contentType: "text/html", order: 3);
builder.Services.AddContentProcessor<UnicodeTagStripProcessor>(contentType: "*", order: 4);
builder.Services.AddContentProcessor<InjectionPatternProcessor>(contentType: "*", order: 5);
builder.Services.AddContentProcessor<SpotlightingProcessor>(contentType: "*", order: 6);
builder.Services.AddScoped<ContentProcessorPipeline>();

// HTTP fetcher
builder.Services.AddOptions<FetchOptions>().BindConfiguration("FetchOptions");
builder.Services.AddSingleton<ISafeHttpFetcher, SafeHttpFetcher>();

// Rate limiting (in-memory only — no Azure dependency in this host)
builder.Services.AddMemoryCache();
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("Safetch:RateLimit"));
builder.Services.AddSingleton<IApiKeyRateLimiter, InMemoryRateLimiter>();

// Fetch service
builder.Services.AddScoped<FetchService>();
builder.Services.AddScoped<IFetchService>(sp =>
    new AuditingFetchService(
        sp.GetRequiredService<FetchService>(),
        sp.GetRequiredService<ILogger<AuditingFetchService>>()));

var app = builder.Build();

// POST /api/fetch
app.MapPost("/api/fetch", async (HttpContext ctx, IFetchService fetchService, IApiKeyRateLimiter rateLimiter, IOptions<RateLimitOptions> rateLimitOptions, CancellationToken ct) =>
{
    FetchRequestDto? dto;
    try
    {
        dto = await ctx.Request.ReadFromJsonAsync<FetchRequestDto>(ct);
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Invalid JSON request body.", errorCode = (string?)null }, statusCode: 400);
    }

    var rawIdentityKey = dto?.IdentityKey;
    var fetchRequest = new FetchRequest
    {
        Url = dto?.Url,
        Mode = ParseMode(dto?.Mode)
    };

    if (string.IsNullOrWhiteSpace(fetchRequest.Url))
        return Results.Json(new { error = "url is required", errorCode = (string?)null }, statusCode: 400);

    if (!string.IsNullOrEmpty(rawIdentityKey) && !IsValidIdentityKey(rawIdentityKey))
        return Results.Json(new { error = "identityKey must be 8 printable ASCII characters or fewer", errorCode = (string?)null }, statusCode: 400);

    fetchRequest.IdentityKey = rawIdentityKey;

    var rateLimit = await rateLimiter.CheckAndIncrementAsync("local", ct);
    if (!rateLimit.Allowed)
    {
        var retryAfter = (int)Math.Ceiling((rateLimit.WindowResetsAt - DateTimeOffset.UtcNow).TotalSeconds);
        ctx.Response.Headers["Retry-After"] = retryAfter.ToString();
        return Results.Json(new { error = $"Rate limit exceeded: {rateLimit.TierLabel}.", errorCode = "RATE_LIMITED" }, statusCode: 429);
    }

    FetchResponse fetchResponse;
    try
    {
        fetchResponse = await fetchService.FetchAsync(fetchRequest, ct);
    }
    catch
    {
        return Results.Json(new { error = "Failed to fetch the requested URL", errorCode = "FETCH_FAILED" }, statusCode: 502);
    }

    if (!fetchResponse.Success)
    {
        var statusCode = fetchResponse.ErrorCode == "BLOCKED" ? 400 : 502;
        return Results.Json(new { error = fetchResponse.ErrorMessage, errorCode = fetchResponse.ErrorCode }, statusCode: statusCode);
    }

    return Results.Json(fetchResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

// GET /api/fetch
app.MapGet("/api/fetch", async (HttpContext ctx, IFetchService fetchService, IApiKeyRateLimiter rateLimiter, IOptions<RateLimitOptions> rateLimitOptions, CancellationToken ct) =>
{
    var query = ctx.Request.Query;
    var url = query["url"].FirstOrDefault();
    var modeStr = query["mode"].FirstOrDefault();
    var identityKey = query["identityKey"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(url)
        || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
        || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.Json(new { error = "url is required and must be a valid absolute HTTP/HTTPS URL", errorCode = (string?)null }, statusCode: 400);
    }

    if (!string.IsNullOrEmpty(identityKey) && !IsValidIdentityKey(identityKey))
        return Results.Json(new { error = "identityKey must be 8 printable ASCII characters or fewer", errorCode = (string?)null }, statusCode: 400);

    var rateLimit = await rateLimiter.CheckAndIncrementAsync("local", ct);
    if (!rateLimit.Allowed)
    {
        var retryAfter = (int)Math.Ceiling((rateLimit.WindowResetsAt - DateTimeOffset.UtcNow).TotalSeconds);
        ctx.Response.Headers["Retry-After"] = retryAfter.ToString();
        return Results.Json(new { error = $"Rate limit exceeded: {rateLimit.TierLabel}.", errorCode = "RATE_LIMITED" }, statusCode: 429);
    }

    var fetchRequest = new FetchRequest
    {
        Url = url,
        Mode = ParseMode(modeStr),
        IdentityKey = identityKey
    };

    FetchResponse fetchResponse;
    try
    {
        fetchResponse = await fetchService.FetchAsync(fetchRequest, ct);
    }
    catch
    {
        return Results.Json(new { error = "Failed to fetch the requested URL", errorCode = "FETCH_FAILED" }, statusCode: 502);
    }

    if (!fetchResponse.Success)
    {
        var statusCode = fetchResponse.ErrorCode == "BLOCKED" ? 400 : 502;
        return Results.Json(new { error = fetchResponse.ErrorMessage, errorCode = fetchResponse.ErrorCode }, statusCode: statusCode);
    }

    return Results.Json(fetchResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.Run();

static ResponseMode ParseMode(string? mode) => mode?.ToLowerInvariant() switch
{
    "readable" => ResponseMode.Readable,
    "text"     => ResponseMode.Text,
    "markdown" => ResponseMode.Markdown,
    _          => ResponseMode.Raw
};

static bool IsValidIdentityKey(string key)
{
    if (key.Length > 8) return false;
    foreach (var c in key)
        if (c < 0x20 || c > 0x7E) return false;
    return true;
}

record FetchRequestDto(string? Url, string? Mode, string? IdentityKey);

public partial class Program { }