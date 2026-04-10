using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Safetch.Core.Services;
using Safetch.Tests.Integration.Fakes;
using Xunit;

namespace Safetch.Tests.Integration;

/// <summary>
/// Uses a fresh factory per test class with MaxFetchesPerWindow=2 so rate limiting
/// triggers after 2 successful requests. InMemoryRateLimiter is Singleton — a fresh
/// factory gives a fresh DI container and thus a fresh limiter instance.
/// </summary>
public class RateLimitTests
{
    private readonly HttpClient _client;

    public RateLimitTests()
    {
        var factory = new RateLimitFactory();
        _client = factory.CreateClient();
    }

    [Fact] // 5.1
    public async Task WithinLimit_RequestsSucceed()
    {
        var r1 = await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        var r2 = await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact] // 5.2
    public async Task ExceedingLimit_Returns429WithRateLimitedErrorCode()
    {
        // Two allowed, third is blocked
        await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        var r3 = await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        var body = await r3.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("RATE_LIMITED", body.GetProperty("errorCode").GetString());
    }

    [Fact] // 5.3
    public async Task ExceedingLimit_RetryAfterHeaderIsPositiveInteger()
    {
        await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        var r3 = await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });

        Assert.True(r3.Headers.TryGetValues("Retry-After", out var values));
        var retryAfter = int.Parse(values!.First());
        Assert.True(retryAfter > 0);
    }

    [Fact] // 5.4
    public async Task RateLimitSharedBetweenGetAndPost()
    {
        // Mix GET and POST — both count against the "local" identity
        await _client.PostAsJsonAsync("/api/fetch", new { url = "https://example.com" });
        await _client.GetAsync("/api/fetch?url=https://example.com");
        var r3 = await _client.GetAsync("/api/fetch?url=https://example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
    }

    // ── Inner factory class ───────────────────────────────────────────────────

    private sealed class RateLimitFactory : WebApplicationFactory<Program>
    {
        private readonly FakeFetchService _fetchService = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Safetch:RateLimit:Limits:0:MaxFetchesPerWindow"] = "2",
                    ["Safetch:RateLimit:Limits:0:Window"] = "00:01:00"
                }));

            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IFetchService));
                services.Remove(descriptor);
                services.AddScoped<IFetchService>(_ => _fetchService);
            });
        }
    }
}
