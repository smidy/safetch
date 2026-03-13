namespace Safetch.Core.Http;

public class FetchOptions
{
    public long MaxResponseBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    public int MaxRedirects { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 15;
}