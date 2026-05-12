namespace Safetch.Core.Models;

public enum SpotlightingMode
{
    /// <summary>
    /// Wraps content in begin/end boundary markers. Default and backward-compatible.
    /// </summary>
    Delimiting,

    /// <summary>
    /// Base64-encodes the entire untrusted content block. More effective at preventing
    /// downstream LLMs from treating fetched content as instructions.
    /// See: Microsoft MSRC "How Microsoft defends against indirect prompt injection" (Jul 2025).
    /// </summary>
    Base64
}
