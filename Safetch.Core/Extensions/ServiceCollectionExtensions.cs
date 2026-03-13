using Microsoft.Extensions.DependencyInjection;
using Safetch.Core.Guards;
using Safetch.Core.Processing;

namespace Safetch.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a guard in the DI pipeline at the specified order.
    /// Guards are Scoped (safe for future per-request dependencies; no captive dependency
    /// because SafeHttpFetcher is Singleton and guards do not depend on it).
    /// </summary>
    public static IServiceCollection AddRequestGuard<T>(
        this IServiceCollection services, int order)
        where T : class, IRequestGuard
    {
        services.AddScoped<T>();
        services.AddScoped<OrderedGuard>(sp => new OrderedGuard(order, sp.GetRequiredService<T>()));
        return services;
    }

    /// <summary>
    /// Registers a content processor in the DI pipeline at the specified order and content type affinity.
    /// Processors are Scoped (safe for per-request dependencies).
    /// </summary>
    public static IServiceCollection AddContentProcessor<T>(
        this IServiceCollection services, string contentType, int order)
        where T : class, IContentProcessor
    {
        services.AddScoped<T>();
        services.AddScoped<OrderedProcessor>(sp =>
            new OrderedProcessor(order, contentType, sp.GetRequiredService<T>()));
        return services;
    }

    /// <summary>
    /// Registers the content processor pipeline as a scoped service.
    /// </summary>
    public static IServiceCollection AddContentProcessorPipeline(this IServiceCollection services)
    {
        services.AddScoped<ContentProcessorPipeline>();
        return services;
    }
}