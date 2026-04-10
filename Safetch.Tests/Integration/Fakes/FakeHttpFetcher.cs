using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Http;

namespace Safetch.Tests.Integration.Fakes;

/// <summary>
/// One-shot fake: NextResult is consumed and cleared after each call,
/// so tests that don't set it get the default HTML success response.
/// </summary>
public class FakeHttpFetcher : ISafeHttpFetcher
{
    public SafeFetchResult? NextResult { get; set; }
    public string? LastUrl { get; private set; }

    public Task<SafeFetchResult> FetchAsync(string url, CancellationToken ct)
    {
        LastUrl = url;

        var result = NextResult;
        NextResult = null;

        return Task.FromResult(result ?? SafeFetchResult.Ok("<p>Hello</p>", "text/html", 200));
    }
}
