namespace Safetch.Core.Models;

public class FetchRequest
{
    public string? Url { get; set; }
    /// <summary>Optional. Used for rate limiting and audit correlation. Advisory — not authenticated.</summary>
    public string? SessionId { get; set; }
    public ResponseMode Mode { get; set; } = ResponseMode.Raw;
}