using Safetch.Core.Processing;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Safetch.Core.Models;

public class FetchResponse
{
    public bool Success { get; init; } = true;
    public string Url { get; init; } = string.Empty;
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
    public string Content { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public long? ContentBytes { get; init; }
    public string? ErrorCode { get; init; }    // "BLOCKED", "FETCH_FAILED", etc.
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<InjectionWarning> InjectionWarnings { get; init; } = System.Array.Empty<InjectionWarning>();
}