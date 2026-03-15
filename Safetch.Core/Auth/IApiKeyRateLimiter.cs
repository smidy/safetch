using System;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Auth;

public interface IApiKeyRateLimiter
{
    Task<RateLimitResult> CheckAndIncrementAsync(string callerIdentity, CancellationToken ct = default);
}

public record RateLimitResult(bool Allowed, int Count, int Limit, DateTimeOffset WindowResetsAt);
