using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;

namespace Safetch.Core.Auth;

/// <summary>
/// In-process rate limiter for use in Development mode.
/// Enforces only the first configured tier. Counters are not persisted.
/// Thread-safe via lock.
/// </summary>
public class InMemoryRateLimiter : IApiKeyRateLimiter
{
    private readonly RateLimitTier _tier;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (int Count, DateTimeOffset WindowStart)> _counters = new();
    private readonly object _lock = new();

    public InMemoryRateLimiter(IOptions<RateLimitOptions> options, Func<DateTimeOffset>? clock = null)
    {
        var opts = options.Value;
        _tier = opts.Limits.Count > 0
            ? opts.Limits[0]
            : new RateLimitTier { MaxFetchesPerWindow = 100, Window = TimeSpan.FromHours(1) };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<RateLimitResult> CheckAndIncrementAsync(string callerIdentity, CancellationToken ct = default)
    {
        var now = _clock();
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % _tier.Window.Ticks),
            TimeSpan.Zero);
        var windowResetsAt = windowStart + _tier.Window;

        lock (_lock)
        {
            if (_counters.TryGetValue(callerIdentity, out var entry))
            {
                if (entry.WindowStart < windowStart)
                    entry = (0, windowStart);
            }
            else
            {
                entry = (0, windowStart);
            }

            if (entry.Count >= _tier.MaxFetchesPerWindow)
            {
                _counters[callerIdentity] = entry;
                return Task.FromResult(new RateLimitResult(false, entry.Count, _tier.MaxFetchesPerWindow, windowResetsAt, _tier.Label));
            }

            entry = (entry.Count + 1, entry.WindowStart);
            _counters[callerIdentity] = entry;
            return Task.FromResult(new RateLimitResult(true, entry.Count, _tier.MaxFetchesPerWindow, windowResetsAt));
        }
    }
}