using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;

namespace Safetch.Core.Auth;

/// <summary>
/// In-process rate limiter for use in Development mode.
/// Counters are not persisted — they reset when the process restarts or when the window rolls over.
/// Thread-safe via lock.
/// </summary>
public class InMemoryRateLimiter : IApiKeyRateLimiter
{
    private readonly RateLimitOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (int Count, DateTimeOffset WindowStart)> _counters = new();
    private readonly object _lock = new();

    public InMemoryRateLimiter(IOptions<RateLimitOptions> options, Func<DateTimeOffset>? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<RateLimitResult> CheckAndIncrementAsync(string callerIdentity, CancellationToken ct = default)
    {
        var now = _clock();
        // Floor current time to the window boundary
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % _options.Window.Ticks),
            TimeSpan.Zero);
        var windowResetsAt = windowStart + _options.Window;

        lock (_lock)
        {
            if (_counters.TryGetValue(callerIdentity, out var entry))
            {
                // If the stored window has expired, reset
                if (entry.WindowStart < windowStart)
                    entry = (0, windowStart);
            }
            else
            {
                entry = (0, windowStart);
            }

            if (entry.Count >= _options.MaxFetchesPerWindow)
            {
                _counters[callerIdentity] = entry;
                return Task.FromResult(new RateLimitResult(false, entry.Count, _options.MaxFetchesPerWindow, windowResetsAt));
            }

            entry = (entry.Count + 1, entry.WindowStart);
            _counters[callerIdentity] = entry;
            return Task.FromResult(new RateLimitResult(true, entry.Count, _options.MaxFetchesPerWindow, windowResetsAt));
        }
    }
}