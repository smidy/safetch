using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Safetch.Core.Http;
using Safetch.Tests.Integration.Fakes;

namespace Safetch.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory that replaces ISafeHttpFetcher with a controllable fake.
/// Guards and the content processor pipeline run for real.
/// Use for testing the full guard pipeline and content transformations through the API.
/// Rate limit is set to 10,000/min so the shared singleton limiter never trips during pipeline tests.
/// </summary>
public class PipelineFactory : WebApplicationFactory<Program>
{
    public FakeHttpFetcher HttpFetcher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Safetch:RateLimit:Limits:0:MaxFetchesPerWindow"] = "10000",
                ["Safetch:RateLimit:Limits:0:Window"] = "00:01:00"
            }));

        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(ISafeHttpFetcher));
            services.Remove(descriptor);
            services.AddSingleton<ISafeHttpFetcher>(HttpFetcher);
        });
    }
}
