using Safetch.Core.Processing;
using System.Collections.Generic;

namespace Safetch.Core.Models;

public class FetchResponse
{
    public bool Success { get; init; } = true;
    public string Url { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public long? ContentBytes { get; init; }
    public string? ErrorCode { get; init; }    // "BLOCKED", "FETCH_FAILED", etc.
    public string? ErrorMessage { get; init; }
    public string? SpotlightingKey { get; init; }

    public IReadOnlyList<InjectionWarning> InjectionWarnings { get; init; } = System.Array.Empty<InjectionWarning>();
}