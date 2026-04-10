using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Safetch.Core.Services;
using Safetch.Tests.Integration.Fakes;

namespace Safetch.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory that replaces IFetchService with a controllable fake.
/// Use for testing the API layer: request parsing, validation, rate limiting, response serialization.
/// Guards and content processors are NOT exercised.
/// Rate limit is set to 10,000/min so the shared singleton limiter never trips during normal API tests.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public FakeFetchService FetchService { get; } = new();

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
            var descriptor = services.Single(d => d.ServiceType == typeof(IFetchService));
            services.Remove(descriptor);
            services.AddScoped<IFetchService>(_ => FetchService);
        });
    }
}
