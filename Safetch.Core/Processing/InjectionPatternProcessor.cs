using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Processing;

public class InjectionPatternProcessor : IContentProcessor
{
    public string Name => "InjectionPattern";

    private static readonly string[] Patterns = new[]
    {
        @"ignore previous instructions",
        @"ignore all previous",
        @"disregard previous",
        @"you are now",
        @"act as",
        @"new persona",
        @"system prompt",
        @"\<\|im_start\|\>",
        @"\[INST\]",
        @"### Instruction",
        @"\<\|system\|\>"
    };

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var warnings = new List<string>();

        foreach (var pattern in Patterns)
        {
            if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
            {
                warnings.Add($"Potential prompt injection detected: pattern '{pattern}' found in fetched content.");
            }
        }

        return Task.FromResult(new ProcessorResult(content, warnings));
    }
}