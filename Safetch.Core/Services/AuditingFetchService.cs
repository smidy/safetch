using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Safetch.Core.Models;

namespace Safetch.Core.Services;

public class AuditingFetchService : IFetchService
{
    private readonly IFetchService _inner;
    private readonly ILogger<AuditingFetchService> _logger;

    public AuditingFetchService(IFetchService inner, ILogger<AuditingFetchService> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("fetch.started {@Event}", new
        {
            event_type = "fetch.started",
            session_id = request.SessionId,
            url_host = GetHost(request.Url),
            timestamp = DateTimeOffset.UtcNow
        });

        FetchResponse response;
        try
        {
            response = await _inner.FetchAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "fetch.error {@Event}", new
            {
                event_type = "fetch.error",
                session_id = request.SessionId,
                url_host = GetHost(request.Url),
                duration_ms = sw.ElapsedMilliseconds
            });
            throw;
        }

        if (!response.Success)
        {
            _logger.LogWarning("fetch.blocked {@Event}", new
            {
                event_type = "fetch.blocked",
                session_id = request.SessionId,
                url_host = GetHost(request.Url),
                error_code = response.ErrorCode,
                duration_ms = sw.ElapsedMilliseconds
            });
        }
        else
        {
            _logger.LogInformation("fetch.completed {@Event}", new
            {
                event_type = "fetch.completed",
                session_id = request.SessionId,
                url_host = GetHost(request.Url),
                status_code = response.StatusCode,
                content_type = response.ContentType,
                content_bytes = response.ContentBytes,
                injection_warning_count = response.InjectionWarnings.Count,
                duration_ms = sw.ElapsedMilliseconds
            });

            foreach (var warning in response.InjectionWarnings)
            {
                _logger.LogWarning("fetch.content_warning {@Event}", new
                {
                    event_type = "fetch.content_warning",
                    session_id = request.SessionId,
                    url_host = GetHost(request.Url),
                    category = warning.Category,
                    pattern = warning.PatternMatched,
                    severity = warning.Severity
                });
            }
        }

        return response;
    }

    private static string GetHost(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown";
}