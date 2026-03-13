namespace Safetch.Core.Http;

public record SafeFetchResult(
    bool Success,
    string? Content,
    string? ContentType,
    int? StatusCode,
    string? ErrorMessage)
{
    public static SafeFetchResult Ok(string content, string contentType, int statusCode)
        => new(true, content, contentType, statusCode, null);
    public static SafeFetchResult Blocked(string reason)
        => new(false, null, null, null, reason);
}