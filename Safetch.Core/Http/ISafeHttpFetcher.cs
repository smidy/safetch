using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Http;

public interface ISafeHttpFetcher
{
    Task<SafeFetchResult> FetchAsync(string url, CancellationToken ct);
}
