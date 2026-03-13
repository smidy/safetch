using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Safetch.Core.Http;
using Safetch.Core.Models;

namespace Safetch.Core.Guards;

/// <summary>
/// Guard #3 — Resolves DNS for the request hostname and blocks any private IP addresses.
/// Remediation #1 from the security report.
/// Also called by SafeHttpFetcher for each redirect hop (not via IRequestGuard — see SafeHttpFetcher).
/// </summary>
public sealed class SsrfGuard : IRequestGuard
{
    private readonly ILogger<SsrfGuard> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolver;

    public string Name => "SsrfGuard";

    public SsrfGuard(ILogger<SsrfGuard> logger,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        _logger = logger;
        _resolver = resolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
    }

    public async ValueTask<GuardResult> CheckAsync(FetchRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return GuardResult.Allow(); // already caught upstream by UrlSchemeGuard

        var host = uri.Host;
        IPAddress[] addresses;
        try
        {
            addresses = await _resolver(host, ct);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("ssrf_guard.dns_failed {Host} {Message}", host, ex.Message);
            return GuardResult.Block($"DNS resolution failed for '{host}': {ex.Message}");
        }

        if (addresses.Length == 0)
            return GuardResult.Block($"DNS resolution returned no addresses for '{host}'.");

        foreach (var ip in addresses)
        {
            if (IpValidator.IsPrivate(ip))
            {
                _logger.LogWarning("ssrf_guard.blocked {Host} {ResolvedIp}", host, ip.ToString());
                return GuardResult.Block($"'{host}' resolves to a private address ({ip}) and cannot be fetched.");
            }
        }

        return GuardResult.Allow();
    }
}