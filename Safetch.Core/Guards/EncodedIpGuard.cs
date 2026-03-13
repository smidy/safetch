using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Http;
using Safetch.Core.Models;

namespace Safetch.Core.Guards;

/// <summary>
/// Guard #2 — Blocks literal private IP addresses in the URL host (including IPv4-mapped IPv6).
/// Remediation #9 from the security report.
///
/// Known limitation: hex/decimal/octal IP literals (e.g. 0x7f000001, 2130706433) are not handled
/// here. .NET's Uri parser rejects most of these; any that slip through are caught by SsrfGuard's
/// DNS resolution returning a private IP.
///
/// Implicitly depends on UrlSchemeGuard having run first (returns Allow on parse failure
/// under the assumption a malformed URL was already blocked). This dependency is enforced by
/// the registered ordering (1 → 2 → 3).
/// </summary>
public sealed class EncodedIpGuard : IRequestGuard
{
    public string Name => "EncodedIpGuard";

    public ValueTask<GuardResult> CheckAsync(FetchRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return ValueTask.FromResult(GuardResult.Allow()); // UrlSchemeGuard already caught this

        var host = uri.Host;

        if (IPAddress.TryParse(host, out var ip))
        {
            return IpValidator.IsPrivate(ip)
                ? ValueTask.FromResult(GuardResult.Block($"Direct IP address '{host}' resolves to a private range."))
                : ValueTask.FromResult(GuardResult.Allow());
        }

        // Host is a name — SsrfGuard will resolve and validate via DNS
        return ValueTask.FromResult(GuardResult.Allow());
    }
}