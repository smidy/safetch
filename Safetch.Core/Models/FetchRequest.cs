namespace Safetch.Core.Models;

public class FetchRequest
{
    public string? Url { get; set; }
    public ResponseMode Mode { get; set; } = ResponseMode.Raw;
}