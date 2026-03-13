using System;

namespace Safetch.Core.Http;

/// <summary>
/// Thrown from LengthLimitedStream when the response body exceeds MaxResponseBytes.
/// Internal to Safetch.Core — never surfaces to API callers.
/// </summary>
internal sealed class ResponseTooLargeException : Exception
{
    public ResponseTooLargeException() : base("Response exceeded maximum allowed size.") { }
}