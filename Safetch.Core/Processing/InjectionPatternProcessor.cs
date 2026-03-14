using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Processing;

public class InjectionPatternProcessor : IContentProcessor
{
    public string Name => "InjectionPattern";

    private static readonly (string Pattern, string Category, InjectionSeverity Severity)[] PatternMap =
    {
        // InstructionOverride — Medium
        (@"ignore previous instructions",       "InstructionOverride", InjectionSeverity.Medium),
        (@"ignore all previous",                "InstructionOverride", InjectionSeverity.Medium),
        (@"disregard previous",                 "InstructionOverride", InjectionSeverity.Medium),

        // PersonaHijacking — Medium
        (@"you are now",                        "PersonaHijacking",    InjectionSeverity.Medium),
        (@"act as",                             "PersonaHijacking",    InjectionSeverity.Medium),
        (@"new persona",                        "PersonaHijacking",    InjectionSeverity.Medium),

        // ModelFormatMarker — Informational
        (@"system prompt",                      "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"\<\|im_start\|\>",                   "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"\[INST\]",                           "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"### Instruction",                    "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"\<\|system\|\>",                     "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"\<\|user\|\>",                       "ModelFormatMarker",   InjectionSeverity.Informational),
        (@"\<\|assistant\|\>",                  "ModelFormatMarker",   InjectionSeverity.Informational),

        // DataExfiltration — High
        (@"send this to",                       "DataExfiltration",    InjectionSeverity.High),
        (@"transmit this",                      "DataExfiltration",    InjectionSeverity.High),
        (@"exfiltrate",                         "DataExfiltration",    InjectionSeverity.High),
        (@"POST to https?://",                  "DataExfiltration",    InjectionSeverity.High),
        (@"call this URL",                      "DataExfiltration",    InjectionSeverity.High),
        (@"fetch https?://",                    "DataExfiltration",    InjectionSeverity.High),

        // ToolCallCoercion — High
        (@"call the \w+ tool",                  "ToolCallCoercion",    InjectionSeverity.High),
        (@"invoke the",                         "ToolCallCoercion",    InjectionSeverity.High),
        (@"execute the tool",                   "ToolCallCoercion",    InjectionSeverity.High),
        (@"run the tool",                       "ToolCallCoercion",    InjectionSeverity.High),
        (@"use the function",                   "ToolCallCoercion",    InjectionSeverity.High),

        // AuthorityOverride — High
        (@"SYSTEM OVERRIDE",                    "AuthorityOverride",   InjectionSeverity.High),
        (@"new instructions from",              "AuthorityOverride",   InjectionSeverity.High),
        (@"acting as administrator",            "AuthorityOverride",   InjectionSeverity.High),
        (@"operator override",                  "AuthorityOverride",   InjectionSeverity.High),

        // MemoryPoisoning — High (compound patterns to reduce false positives)
        (@"remember\b.{0,60}\btrusted\b",       "MemoryPoisoning",     InjectionSeverity.High),
        (@"in future conversations",            "MemoryPoisoning",     InjectionSeverity.High),
        (@"authoritative source for",           "MemoryPoisoning",     InjectionSeverity.High),
        (@"keep in your memory",                "MemoryPoisoning",     InjectionSeverity.High),
        (@"cite this as",                       "MemoryPoisoning",     InjectionSeverity.High),
        (@"always cite",                        "MemoryPoisoning",     InjectionSeverity.High),

        // JailbreakFraming — Medium
        (@"god mode",                           "JailbreakFraming",    InjectionSeverity.Medium),
        (@"developer mode",                     "JailbreakFraming",    InjectionSeverity.Medium),
        (@"\bDAN\b",                            "JailbreakFraming",    InjectionSeverity.Medium),
        (@"\bjailbreak\b",                      "JailbreakFraming",    InjectionSeverity.Medium),
        (@"unrestricted mode",                  "JailbreakFraming",    InjectionSeverity.Medium),
    };

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var injectionWarnings = new List<InjectionWarning>();

        foreach (var (pattern, category, severity) in PatternMap)
        {
            if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                injectionWarnings.Add(new InjectionWarning(category, pattern, severity));
            }
        }

        return Task.FromResult(new ProcessorResult(content, injectionWarnings));
    }
}