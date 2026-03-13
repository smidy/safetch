using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Safetch.Core.Guards;
using Safetch.Core.Http;
using Safetch.Core.Models;
using Safetch.Core.Processing;

namespace Safetch.Core.Services;

public class FetchService : IFetchService
{
    private readonly IEnumerable<OrderedGuard> _guards;
    private readonly SafeHttpFetcher _fetcher;
    private readonly ILogger<FetchService> _logger;
    private readonly ContentProcessorPipeline _pipeline;

    public FetchService(
        IEnumerable<OrderedGuard> guards,
        SafeHttpFetcher fetcher,
        ILogger<FetchService> logger,
        ContentProcessorPipeline pipeline)
    {
        _guards = guards;
        _fetcher = fetcher;
        _logger = logger;
        _pipeline = pipeline;
    }

    public async Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken ct = default)
    {
        // 1. Run guards in order
        // ErrorCode = "BLOCKED" for guard rejections (URL policy, SSRF, encoded IP)
        foreach (var ordered in _guards.OrderBy(g => g.Order))
        {
            var result = await ordered.Guard.CheckAsync(request, ct);
            if (!result.Allowed)
                return new FetchResponse
                {
                    Success = false,
                    SessionId = request.SessionId,
                    ErrorCode = "BLOCKED",
                    ErrorMessage = result.Reason
                };
        }

        // 2. Fetch
        // ErrorCode = "FETCH_FAILED" for fetcher-level failures: DNS rebinding (ConnectCallback),
        // too many redirects, redirect SSRF, response too large, or network errors.
        SafeFetchResult fetched;
        try
        {
            fetched = await _fetcher.FetchAsync(request.Url!, ct);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "safe_fetch.unexpected_error {Url}", request.Url);
            return new FetchResponse
            {
                Success = false,
                SessionId = request.SessionId,
                ErrorCode = "FETCH_FAILED",
                ErrorMessage = "An unexpected error occurred while fetching the URL."
            };
        }

        if (!fetched.Success)
            return new FetchResponse
            {
                Success = false,
                SessionId = request.SessionId,
                ErrorCode = "FETCH_FAILED",
                ErrorMessage = fetched.ErrorMessage
            };

        // 3. Content processing (HTML → plain text etc.)
        var content = fetched.Content ?? string.Empty;
        
        // Parse MIME type from ContentType (strip parameters, lowercase)
        var contentType = fetched.ContentType;
        var mimeType = "text/plain";
        if (!string.IsNullOrEmpty(contentType))
        {
            var semicolon = contentType.IndexOf(';');
            if (semicolon >= 0)
                mimeType = contentType.Substring(0, semicolon).Trim().ToLowerInvariant();
            else
                mimeType = contentType.Trim().ToLowerInvariant();
        }
        
        // Build MIME type, extending it for readable/text modes
        var effectiveMimeType = mimeType;
        if (mimeType == "text/html")
        {
            effectiveMimeType = request.Mode switch
            {
                ResponseMode.Readable => "text/html+readable",
                ResponseMode.Text     => "text/html+text",
                ResponseMode.Markdown => "text/html+markdown",
                _                     => mimeType
            };
        }

        var context = new ProcessingContext(effectiveMimeType, request.Url!);
        var processorResult = await _pipeline.RunAsync(content, context, ct);
        
        return new FetchResponse
        {
            Success = true,
            SessionId = request.SessionId,
            Url = request.Url!,
            Content = processorResult.Content,
            StatusCode = fetched.StatusCode ?? 0,
            ContentType = fetched.ContentType,
            ContentBytes = processorResult.Content.Length,
            Warnings = processorResult.Warnings,
            InjectionWarnings = processorResult.InjectionWarnings
        };
    }
}