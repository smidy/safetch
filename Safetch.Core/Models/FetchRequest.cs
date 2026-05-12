namespace Safetch.Core.Models;

public class FetchRequest
{
    public string? Url { get; set; }
    public ResponseMode Mode { get; set; } = ResponseMode.Raw;
    public string? IdentityKey { get; set; }
    public SpotlightingMode SpotlightingMode { get; set; } = SpotlightingMode.Delimiting;
}