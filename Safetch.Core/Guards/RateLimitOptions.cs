using System;

namespace Safetch.Core.Guards;

public class RateLimitOptions
{
    public int MaxFetchesPerWindow { get; set; } = 100;
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}
