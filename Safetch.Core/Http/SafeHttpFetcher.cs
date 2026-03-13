using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Safetch.Core.Guards;

namespace Safetch.Core.Http;

/// <summary>
/// Singleton HTTP fetcher with SSRF protection, per-redirect SSRF validation,
/// IP pinning via SocketsHttpHandler.ConnectCallback, and streaming body size enforcement.
/// Owns the HttpClient lifecycle — do not register a named/typed HttpClient for it.
/// </summary>
public sealed class SafeHttpFetcher
{
    private readonly FetchOptions _options;
    private readonly ILogger<SafeHttpFetcher> _logger;
    private readonly HttpClient _httpClient;

    public SafeHttpFetcher(IOptions<FetchOptions> options, ILogger<SafeHttpFetcher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,         // manual redirect loop
            ConnectCallback = PinToValidatedIpAsync,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
        // HttpClient.Timeout applies to the total request chain (all redirect hops combined),
        // not per hop. Per-hop timeout is not enforced in this sprint.
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// DNS-pinning callback. Resolves DNS, validates all IPs, connects to the first valid one.
    /// Throws SsrfException if all IPs are private (DNS rebinding attempt).
    /// Note: session ID is NOT available here — this is a .NET constraint.
    /// </summary>
    private static async ValueTask<Stream> PinToValidatedIpAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);

        var valid = addresses.FirstOrDefault(ip => !IpValidator.IsPrivate(ip));

        if (valid is null)
        {
            var reason = addresses.Length == 0
                ? $"DNS returned no addresses for '{context.DnsEndPoint.Host}'"
                : $"All addresses for '{context.DnsEndPoint.Host}' are private (DNS rebinding attempt blocked)";
            throw new SsrfException(reason);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(valid, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<SafeFetchResult> FetchAsync(string url, CancellationToken ct)
    {
        var current = url;
        var hopsRemaining = _options.MaxRedirects;

        while (true)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (SsrfException ex)
            {
                // Thrown from ConnectCallback (DNS rebinding blocked)
                _logger.LogWarning("safe_fetch.dns_pin_blocked {Url} {Reason}", current, ex.Message);
                return SafeFetchResult.Blocked(ex.Message);
            }

            if (response.StatusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
            {
                if (hopsRemaining <= 0)
                    return SafeFetchResult.Blocked("Too many redirects.");

                var location = response.Headers.Location;
                if (location is null)
                    return SafeFetchResult.Blocked("Redirect with no Location header.");

                // Resolve relative redirects against current URL
                var redirectUrl = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(current), location).ToString();

                // Per-hop SSRF validation
                // Note: mirrors SsrfGuard logic but operates on a raw URL string.
                // Future: extract shared static SsrfValidator.CheckHostAsync to avoid duplication.
                var ssrfCheck = await CheckRedirectUrlAsync(redirectUrl, ct);
                if (!ssrfCheck.Allowed)
                {
                    _logger.LogWarning("safe_fetch.redirect_blocked {From} {To} {Reason}",
                        current, redirectUrl, ssrfCheck.Reason);
                    return SafeFetchResult.Blocked(ssrfCheck.Reason!);
                }

                current = redirectUrl;
                hopsRemaining--;
                response.Dispose();
                continue;
            }

            return await ReadBodyAsync(response, ct);
        }
    }

    private async Task<SafeFetchResult> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var statusCode = (int)response.StatusCode;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        // StreamReader handles multi-byte UTF-8 correctly across buffer boundaries
        using var reader = new StreamReader(
            new LengthLimitedStream(stream, _options.MaxResponseBytes),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 8192,
            leaveOpen: false);

        string body;
        try
        {
            body = await reader.ReadToEndAsync(ct);
        }
        catch (ResponseTooLargeException)
        {
            return SafeFetchResult.Blocked($"Response exceeded maximum size of {_options.MaxResponseBytes / 1024 / 1024} MB.");
        }

        return SafeFetchResult.Ok(body, contentType, statusCode);
    }

    /// <summary>
    /// Per-hop SSRF check for redirect targets: resolves DNS and validates IPs.
    /// Cannot use the IRequestGuard interface — operates on a raw URL string, not a FetchRequest.
    /// </summary>
    private static async Task<GuardResult> CheckRedirectUrlAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return GuardResult.Block("Redirect URL is malformed.");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return GuardResult.Block($"Redirect to scheme '{uri.Scheme}' is not permitted.");

        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(uri.Host, ct); }
        catch (SocketException ex) { return GuardResult.Block($"DNS resolution failed: {ex.Message}"); }

        if (addresses.Length == 0)
            return GuardResult.Block("DNS returned no addresses for redirect target.");

        foreach (var ip in addresses)
            if (IpValidator.IsPrivate(ip))
                return GuardResult.Block($"Redirect target resolves to private address ({ip}).");

        return GuardResult.Allow();
    }
}