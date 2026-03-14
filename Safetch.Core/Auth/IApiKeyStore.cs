using System;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Auth;

public interface IApiKeyStore
{
    /// <summary>
    /// Gets the existing API key for a GitHub user, or null if none exists.
    /// </summary>
    Task<string?> GetKeyAsync(string githubUserId, CancellationToken ct = default);

    /// <summary>
    /// Creates and stores a new API key for the given GitHub user. Returns the new key.
    /// </summary>
    Task<string> CreateKeyAsync(string githubUserId, string githubLogin, CancellationToken ct = default);

    /// <summary>
    /// Looks up the GitHub user ID associated with an API key. Returns null if not found.
    /// </summary>
    Task<string?> ValidateKeyAsync(string apiKey, CancellationToken ct = default);
}