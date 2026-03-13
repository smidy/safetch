using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Safetch.Core.Extensions;
using Safetch.Core.Guards;
using Safetch.Core.Http;
using Safetch.Core.Processing;
using Safetch.Core.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Security pipeline — guards run in Order sequence (1 → 2 → 3 → 4)
        services.AddRequestGuard<UrlSchemeGuard>(order: 1);
        services.AddRequestGuard<EncodedIpGuard>(order: 2);
        services.AddRequestGuard<SsrfGuard>(order: 3);
        services.AddRequestGuard<RateLimitGuard>(order: 4);

        // Content processing pipeline — runs after fetch, before returning to caller
        // Readable extraction — only active for mode=readable and mode=text
        services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+readable", order: 1);
        services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+text", order: 1);
        // Markdown mode: Readability extraction → sanitise → convert to Markdown
        services.AddContentProcessor<ReadableContentProcessor>(contentType: "text/html+markdown", order: 1);
        services.AddContentProcessor<HtmlSanitizerProcessor>(contentType: "text/html+markdown", order: 2);
        services.AddContentProcessor<HtmlToMarkdownProcessor>(contentType: "text/html+markdown", order: 3);
        services.AddContentProcessor<HtmlSanitizerProcessor>(contentType: "text/html", order: 2);
        services.AddContentProcessor<HtmlToMarkdownProcessor>(contentType: "text/html", order: 3);
        services.AddContentProcessor<UnicodeTagStripProcessor>(contentType: "*", order: 4);
        services.AddContentProcessor<InjectionPatternProcessor>(contentType: "*", order: 5);
        services.AddContentProcessor<SpotlightingProcessor>(contentType: "*", order: 6);
        services.AddScoped<ContentProcessorPipeline>();

        // SafeHttpFetcher is Singleton — owns its HttpClient lifecycle
        services.AddOptions<FetchOptions>()
    .BindConfiguration("FetchOptions");
        services.AddSingleton<SafeHttpFetcher>();

        // IMemoryCache for rate limiting (Singleton — safe to inject into Scoped guards)
        services.AddMemoryCache();
        services.Configure<RateLimitOptions>(context.Configuration.GetSection("RateLimit"));

        // FetchService wrapped by AuditingFetchService decorator
        services.AddScoped<FetchService>();
        services.AddScoped<IFetchService>(sp =>
            new AuditingFetchService(
                sp.GetRequiredService<FetchService>(),
                sp.GetRequiredService<ILogger<AuditingFetchService>>()));

    })
    .Build();

host.Run();