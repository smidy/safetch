using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
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

        if (_options.Limits.Count == 0)
            return new RateLimitResult(true, 0, 0, now.AddHours(1));

        RateLimitResult? lastResult = null;
        foreach (var tier in _options.Limits)
        {
            var result = await CheckAndIncrementTierAsync(callerIdentity, tier, now, ct);
            if (!result.Allowed)
                return result;
            lastResult = result;
        }

        return lastResult!;
    }

    private async Task<RateLimitResult> CheckAndIncrementTierAsync(
        string callerIdentity,
        RateLimitTier tier,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var windowStart = new DateTimeOffset(
            now.UtcDateTime - TimeSpan.FromTicks(now.UtcDateTime.Ticks % tier.Window.Ticks),
            TimeSpan.Zero);
        var windowKey = windowStart.ToUnixTimeSeconds().ToString();
        var partitionKey = "ratelimit";
        var rowKey = $"{callerIdentity}:{tier.Window.Ticks}:{windowKey}";
        var windowResetsAt = windowStart + tier.Window;

        // Step 1: Try to read existing entity
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, null, ct);
            var entity = response.Value;
            var count = entity.GetInt32("Count") ?? 0;

            if (count >= tier.MaxFetchesPerWindow)
                return new RateLimitResult(false, count, tier.MaxFetchesPerWindow, windowResetsAt, tier.Label);

            // Step 2a: Attempt conditional update
            var updatedEntity = new TableEntity(partitionKey, rowKey)
            {
                ["Count"] = count + 1
            };
            try
            {
                await _table.UpdateEntityAsync(updatedEntity, entity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, count + 1, tier.MaxFetchesPerWindow, windowResetsAt);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Retry once on ETag mismatch
                var retryResponse = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var retryEntity = retryResponse.Value;
                var retryCount = retryEntity.GetInt32("Count") ?? 0;
                if (retryCount >= tier.MaxFetchesPerWindow)
                    return new RateLimitResult(false, retryCount, tier.MaxFetchesPerWindow, windowResetsAt, tier.Label);

                var retryUpdatedEntity = new TableEntity(partitionKey, rowKey)
                {
                    ["Count"] = retryCount + 1
                };
                await _table.UpdateEntityAsync(retryUpdatedEntity, retryEntity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, retryCount + 1, tier.MaxFetchesPerWindow, windowResetsAt);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Entity doesn't exist — treat as count = 0
        }

        // Step 2b: Entity didn't exist — attempt insert
        var newEntity = new TableEntity(partitionKey, rowKey)
        {
            ["Count"] = 1
        };
        try
        {
            await _table.AddEntityAsync(newEntity, ct);
            return new RateLimitResult(true, 1, tier.MaxFetchesPerWindow, windowResetsAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Conflict — someone else inserted; retry read
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var entity = response.Value;
                var count = entity.GetInt32("Count") ?? 0;
                if (count >= tier.MaxFetchesPerWindow)
                    return new RateLimitResult(false, count, tier.MaxFetchesPerWindow, windowResetsAt, tier.Label);

                var updatedEntity = new TableEntity(partitionKey, rowKey)
                {
                    ["Count"] = count + 1
                };
                await _table.UpdateEntityAsync(updatedEntity, entity.ETag, TableUpdateMode.Replace, ct);
                return new RateLimitResult(true, count + 1, tier.MaxFetchesPerWindow, windowResetsAt);
            }
            catch (RequestFailedException ex2) when (ex2.Status == 404)
            {
                await _table.AddEntityAsync(newEntity, ct);
                return new RateLimitResult(true, 1, tier.MaxFetchesPerWindow, windowResetsAt);
            }
        }
    }
}