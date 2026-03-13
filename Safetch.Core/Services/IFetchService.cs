using Safetch.Core.Models;

namespace Safetch.Core.Services;

public interface IFetchService
{
    Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken ct = default);
}