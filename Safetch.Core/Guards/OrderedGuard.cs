namespace Safetch.Core.Guards;

public record OrderedGuard(int Order, IRequestGuard Guard);