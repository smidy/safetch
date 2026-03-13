using System;

namespace Safetch.Core.Guards;

public class RateLimitOptions
{
    public int MaxFetchesPerSession { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}