using System;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Auth;

public interface IApiKeyRateLimiter
{
    Task<RateLimitResult> CheckAndIncrementAsync(string callerIdentity, CancellationToken ct = default);
}

/// <param name="Allowed">Whether the request is permitted.</param>
/// <param name="Count">Current count in the violated (or highest) tier window.</param>
/// <param name="Limit">The limit of the violated (or highest) tier.</param>
/// <param name="WindowResetsAt">When the violated (or highest) tier window resets.</param>
/// <param name="TierLabel">Human-readable label of the violated tier, e.g. "10 requests per minute". Null if allowed.</param>
public record RateLimitResult(bool Allowed, int Count, int Limit, DateTimeOffset WindowResetsAt, string? TierLabel = null);