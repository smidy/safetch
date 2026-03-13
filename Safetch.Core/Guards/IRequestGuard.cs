using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Models;

namespace Safetch.Core.Guards;

public interface IRequestGuard
{
    string Name { get; }
    ValueTask<GuardResult> CheckAsync(FetchRequest request, CancellationToken ct);
}

public record GuardResult(bool Allowed, string? Reason = null)
{
    public static GuardResult Allow() => new(true);
    public static GuardResult Block(string reason) => new(false, reason);
}