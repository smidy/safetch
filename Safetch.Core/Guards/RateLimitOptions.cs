using System;
using System.Collections.Generic;

namespace Safetch.Core.Guards;

public class RateLimitOptions
{
    public List<RateLimitTier> Limits { get; set; } = new();
}

public class RateLimitTier
{
    public int MaxFetchesPerWindow { get; set; }
    public TimeSpan Window { get; set; }

    /// <summary>Human-readable label used in 429 error messages, e.g. "10 requests per minute".</summary>
    public string Label => $"{MaxFetchesPerWindow} requests per {FormatWindow(Window)}";

    private static string FormatWindow(TimeSpan ts)
    {
        if (ts.TotalDays >= 1 && ts.TotalDays % 1 == 0)
            return ts.TotalDays == 1 ? "day" : $"{(int)ts.TotalDays} days";
        if (ts.TotalHours >= 1 && ts.TotalHours % 1 == 0)
            return ts.TotalHours == 1 ? "hour" : $"{(int)ts.TotalHours} hours";
        if (ts.TotalMinutes >= 1 && ts.TotalMinutes % 1 == 0)
            return ts.TotalMinutes == 1 ? "minute" : $"{(int)ts.TotalMinutes} minutes";
        return ts.ToString();
    }
}