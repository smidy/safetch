using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Models;
using Safetch.Core.Services;

namespace Safetch.Tests.Integration.Fakes;

/// <summary>
/// One-shot fake: NextResponse/NextException are consumed and cleared after each call,
/// so tests that don't set them get the default success response.
/// </summary>
public class FakeFetchService : IFetchService
{
    public FetchResponse? NextResponse { get; set; }
    public Exception? NextException { get; set; }
    public FetchRequest? LastRequest { get; private set; }

    public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken ct = default)
    {
        LastRequest = request;

        var ex = NextException;
        NextException = null;
        if (ex is not null) throw ex;

        var response = NextResponse;
        NextResponse = null;

        return Task.FromResult(response ?? new FetchResponse
        {
            Success = true,
            Url = request.Url!,
            Content = "test content",
            StatusCode = 200,
            ContentType = "text/plain",
            ContentBytes = 12
        });
    }
}
