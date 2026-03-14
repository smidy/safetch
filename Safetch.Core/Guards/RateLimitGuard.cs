using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Safetch.Core.Models;

namespace Safetch.Core.Guards;

public class RateLimitGuard : IRequestGuard
{
    private readonly IMemoryCache _cache;
    private readonly RateLimitOptions _options;

    public string Name => "RateLimitGuard";

    public RateLimitGuard(IMemoryCache cache, IOptions<RateLimitOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public ValueTask<GuardResult> CheckAsync(FetchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return ValueTask.FromResult(GuardResult.Allow());

        var key = $"ratelimit:{request.SessionId}";
        var counter = _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = _options.Window;
            return new long[] { 0 };
        })!;

        var count = Interlocked.Increment(ref counter[0]);

        if (count > _options.MaxFetchesPerWindow)
            return ValueTask.FromResult(GuardResult.Block(
                $"Rate limit exceeded: maximum {_options.MaxFetchesPerWindow} fetches per window per hour."));

        return ValueTask.FromResult(GuardResult.Allow());
    }
}