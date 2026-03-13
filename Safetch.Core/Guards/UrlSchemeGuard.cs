using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Models;

namespace Safetch.Core.Guards;

/// <summary>
/// Guard #1 — Blocks non-http/https schemes and malformed URLs.
/// Remediation #17 from the security report.
/// </summary>
public sealed class UrlSchemeGuard : IRequestGuard
{
    public string Name => "UrlSchemeGuard";

    public ValueTask<GuardResult> CheckAsync(FetchRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return ValueTask.FromResult(
                GuardResult.Block($"URL scheme '{uri?.Scheme}' is not permitted. Only http and https are allowed."));
        }

        return ValueTask.FromResult(GuardResult.Allow());
    }
}