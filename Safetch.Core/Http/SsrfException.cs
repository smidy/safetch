using System;

namespace Safetch.Core.Http;

/// <summary>
/// Thrown from SafeHttpFetcher.ConnectCallback when DNS rebinding is detected.
/// Internal to Safetch.Core — never surfaces to API callers.
/// </summary>
internal sealed class SsrfException : Exception
{
    public SsrfException(string message) : base(message) { }
}