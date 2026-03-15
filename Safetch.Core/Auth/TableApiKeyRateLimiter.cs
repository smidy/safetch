using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;

namespace Safetch.Core.Auth;

public class TableApiKeyRateLimiter : IApiKeyRateLimiter
{
    private readonly TableClient _table;
    private readonly RateLimitOptions _options;
    private readonly ILogger<TableApiKeyRateLimiter> _logger;

    public TableApiKeyRateLimiter(
        TableClient table,
        IOptions<RateLimitOptions> options,
        ILogger<TableApiKeyRateLimiter> logger)
    {
        _table = table;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckAndIncrementAsync(string callerIdentity, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % _options.Window.Ticks),
            TimeSpan.Zero);
        var windowKey = windowStart.ToUnixTimeSeconds().ToString();
        var partitionKey = "ratelimit";
        var rowKey = $"{callerIdentity}:{windowKey}";
        var windowResetsAt = windowStart + _options.Window;

        // Step 1: Try to read existing entity
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, null, ct);
            var entity = response.Value;
            var count = entity.GetInt32("Count") ?? 0;

            if (count >= _options.MaxFetchesPerWindow)
                return new RateLimitResult(false, count, _options.MaxFetchesPerWindow, windowResetsAt);

            // Step 2a: Attempt conditional update
            var updatedEntity = new TableEntity(partitionKey, rowKey)
            {
                ["Count"] = count + 1
            };
            try
            {
                await _table.UpdateEntityAsync(updatedEntity, entity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, count + 1, _options.MaxFetchesPerWindow, windowResetsAt);
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // Precondition Failed
            {
                // Retry once on ETag mismatch
                var retryResponse = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var retryEntity = retryResponse.Value;
                var retryCount = retryEntity.GetInt32("Count") ?? 0;
                if (retryCount >= _options.MaxFetchesPerWindow)
                    return new RateLimitResult(false, retryCount, _options.MaxFetchesPerWindow, windowResetsAt);

                var retryUpdatedEntity = new TableEntity(partitionKey, rowKey)
                {
                    ["Count"] = retryCount + 1
                };
                await _table.UpdateEntityAsync(retryUpdatedEntity, retryEntity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, retryCount + 1, _options.MaxFetchesPerWindow, windowResetsAt);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Entity doesn't exist — treat as count = 0
        }

        // Step 2b: Entity didn't exist or we're here after 404 — attempt insert
        var newEntity = new TableEntity(partitionKey, rowKey)
        {
            ["Count"] = 1
        };
        try
        {
            await _table.AddEntityAsync(newEntity, ct);
            return new RateLimitResult(true, 1, _options.MaxFetchesPerWindow, windowResetsAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 409) // Conflict — someone else inserted
        {
            // Retry once on conflict
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var entity = response.Value;
                var count = entity.GetInt32("Count") ?? 0;
                if (count >= _options.MaxFetchesPerWindow)
                    return new RateLimitResult(false, count, _options.MaxFetchesPerWindow, windowResetsAt);

                var updatedEntity = new TableEntity(partitionKey, rowKey)
                {
                    ["Count"] = count + 1
                };
                await _table.UpdateEntityAsync(updatedEntity, entity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, count + 1, _options.MaxFetchesPerWindow, windowResetsAt);
            }
            catch (RequestFailedException ex2) when (ex2.Status == 404)
            {
                // Should not happen — but fallback to insert again
                await _table.AddEntityAsync(newEntity, ct);
                return new RateLimitResult(true, 1, _options.MaxFetchesPerWindow, windowResetsAt);
            }
        }
    }
}
