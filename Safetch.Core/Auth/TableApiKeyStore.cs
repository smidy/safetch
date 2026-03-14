using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace Safetch.Core.Auth;

/// <summary>
/// Stores API keys in Azure Table Storage.
/// Two row types per user:
///   - PartitionKey="github", RowKey=githubUserId  â†’ holds ApiKey, GitHubLogin, CreatedAt
///   - PartitionKey="apikey", RowKey=apiKey        â†’ holds GitHubUserId (for O(1) reverse lookup)
/// </summary>
public class TableApiKeyStore : IApiKeyStore
{
    private readonly TableClient _table;
    private readonly ILogger<TableApiKeyStore> _logger;

    public TableApiKeyStore(TableClient table, ILogger<TableApiKeyStore> logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<string?> GetKeyAsync(string githubUserId, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>("github", githubUserId, cancellationToken: ct);
            return response.Value.GetString("ApiKey");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<string> CreateKeyAsync(string githubUserId, string githubLogin, CancellationToken ct = default)
    {
        // Generate a cryptographically random 32-byte key, base64url-encoded (no padding)
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var apiKey = Convert.ToBase64String(keyBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var now = DateTimeOffset.UtcNow;

        // Primary row: github/userId â†’ ApiKey, GitHubLogin, CreatedAt
        var userRow = new TableEntity("github", githubUserId)
        {
            ["ApiKey"] = apiKey,
            ["GitHubLogin"] = githubLogin,
            ["CreatedAt"] = now
        };

        // Reverse-lookup row: apikey/apiKey â†’ GitHubUserId
        var keyRow = new TableEntity("apikey", apiKey)
        {
            ["GitHubUserId"] = githubUserId,
            ["CreatedAt"] = now
        };

        await _table.UpsertEntityAsync(userRow, TableUpdateMode.Replace, ct);
        await _table.UpsertEntityAsync(keyRow, TableUpdateMode.Replace, ct);

        _logger.LogInformation("Created API key for GitHub user {UserId} ({Login})", githubUserId, githubLogin);
        return apiKey;
    }

    public async Task<string?> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>("apikey", apiKey, cancellationToken: ct);
            return response.Value.GetString("GitHubUserId");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}