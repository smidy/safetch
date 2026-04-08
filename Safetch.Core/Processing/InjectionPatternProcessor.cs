using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Safetch.Core.Processing;

public class InjectionPatternProcessor : IContentProcessor
{
    public string Name => "InjectionPattern";

    // Timeout long enough for legitimate large content; short enough to abort
    // catastrophic backtracking before it blocks the request pipeline.
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromMilliseconds(200);

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled, _matchTimeout);

    // Compiled at startup; each carries a per-match timeout to guard against
    // catastrophic backtracking on adversarially crafted content.
    private static readonly (Regex Pattern, string Category, InjectionSeverity Severity)[] _patterns =
    {
        // InstructionOverride — Medium
        (R(@"ignore previous instructions"),       "InstructionOverride", InjectionSeverity.Medium),
        (R(@"ignore all previous"),                "InstructionOverride", InjectionSeverity.Medium),
        (R(@"disregard previous"),                 "InstructionOverride", InjectionSeverity.Medium),
        (R(@"forget everything"),                  "InstructionOverride", InjectionSeverity.Medium),
        (R(@"new instructions follow"),            "InstructionOverride", InjectionSeverity.Medium),
        (R(@"override all instructions"),          "InstructionOverride", InjectionSeverity.Medium),
        (R(@"clear your instructions"),            "InstructionOverride", InjectionSeverity.Medium),

        // PersonaHijacking — Medium
        (R(@"you are now"),                        "PersonaHijacking",    InjectionSeverity.Medium),
        (R(@"act as"),                             "PersonaHijacking",    InjectionSeverity.Medium),
        (R(@"new persona"),                        "PersonaHijacking",    InjectionSeverity.Medium),
        (R(@"pretend you are"),                    "PersonaHijacking",    InjectionSeverity.Medium),
        (R(@"roleplay as"),                        "PersonaHijacking",    InjectionSeverity.Medium),

        // ModelFormatMarker — Informational
        (R(@"system prompt"),                      "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"\<\|im_start\|\>"),                   "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"\[INST\]"),                           "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"### Instruction"),                    "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"\<\|system\|\>"),                     "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"\<\|user\|\>"),                       "ModelFormatMarker",   InjectionSeverity.Informational),
        (R(@"\<\|assistant\|\>"),                  "ModelFormatMarker",   InjectionSeverity.Informational),

        // DataExfiltration — High
        (R(@"send this to"),                       "DataExfiltration",    InjectionSeverity.High),
        (R(@"transmit this"),                      "DataExfiltration",    InjectionSeverity.High),
        (R(@"exfiltrate"),                         "DataExfiltration",    InjectionSeverity.High),
        (R(@"POST to https?://"),                  "DataExfiltration",    InjectionSeverity.High),
        (R(@"call this URL"),                      "DataExfiltration",    InjectionSeverity.High),
        (R(@"fetch https?://"),                    "DataExfiltration",    InjectionSeverity.High),
        (R(@"\bGET https?://"),                     "DataExfiltration",    InjectionSeverity.High),
        (R(@"\bcurl\s+https?://"),                 "DataExfiltration",    InjectionSeverity.High),
        (R(@"\bwget\s+https?://"),                 "DataExfiltration",    InjectionSeverity.High),

        // ToolCallCoercion — High
        (R(@"call the \w+ tool"),                  "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"invoke the"),                         "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"execute the tool"),                   "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"run the tool"),                       "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"use the function"),                   "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"<tool_call>"),                        "ToolCallCoercion",    InjectionSeverity.High),
        (R(@"""tool_name""\s*:"),                   "ToolCallCoercion",    InjectionSeverity.High),

        // AuthorityOverride — High
        (R(@"SYSTEM OVERRIDE"),                    "AuthorityOverride",   InjectionSeverity.High),
        (R(@"new instructions from"),              "AuthorityOverride",   InjectionSeverity.High),
        (R(@"acting as administrator"),            "AuthorityOverride",   InjectionSeverity.High),
        (R(@"operator override"),                  "AuthorityOverride",   InjectionSeverity.High),

        // MemoryPoisoning — High
        (R(@"remember\b.{0,60}\btrusted\b"),       "MemoryPoisoning",     InjectionSeverity.High),
        (R(@"in future conversations"),            "MemoryPoisoning",     InjectionSeverity.High),
        (R(@"authoritative source for"),           "MemoryPoisoning",     InjectionSeverity.High),
        (R(@"keep in your memory"),                "MemoryPoisoning",     InjectionSeverity.High),
        (R(@"cite this as"),                       "MemoryPoisoning",     InjectionSeverity.High),
        (R(@"always cite"),                        "MemoryPoisoning",     InjectionSeverity.High),

        // JailbreakFraming — Medium
        (R(@"god mode"),                           "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"developer mode"),                     "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"\bDAN\b"),                            "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"\bjailbreak\b"),                      "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"unrestricted mode"),                  "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"do anything now"),                    "JailbreakFraming",    InjectionSeverity.Medium),
        (R(@"without any restrictions"),           "JailbreakFraming",    InjectionSeverity.Medium),
    };

    private readonly ILogger<InjectionPatternProcessor> _logger;

    // Constructor used by DI
    public InjectionPatternProcessor(ILogger<InjectionPatternProcessor> logger)
    {
        _logger = logger;
    }

    // Constructor used in tests (no logging)
    public InjectionPatternProcessor() : this(Microsoft.Extensions.Logging.Abstractions.NullLogger<InjectionPatternProcessor>.Instance)
    {
    }

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var injectionWarnings = new List<InjectionWarning>();

        foreach (var (regex, category, severity) in _patterns)
        {
            bool matched;
            try
            {
                matched = regex.IsMatch(content);
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning(
                    "InjectionPatternProcessor: regex timeout on pattern {Pattern} — treating as no match",
                    regex.ToString());
                matched = false; // timed out — treat as no match, do not block request
            }

            if (matched)
                injectionWarnings.Add(new InjectionWarning(category, regex.ToString(), severity));
        }

        return Task.FromResult(new ProcessorResult(content, injectionWarnings));
    }
}
