using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Processing;

public class UnicodeTagStripProcessor : IContentProcessor
{
    public string Name => "UnicodeTagStrip";

    // U+E0000–U+E007F encoded as UTF-16 surrogate pairs: lead surrogate \uDB40, trail \uDC00–\uDC7F
    private static readonly Regex TagsBlock = new(@"[\uDB40][\uDC00-\uDC7F]", RegexOptions.Compiled);

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var matches = TagsBlock.Matches(content);
        if (matches.Count == 0)
        {
            return Task.FromResult(new ProcessorResult(content, new List<InjectionWarning>()));
        }

        var stripped = TagsBlock.Replace(content, "");
        return Task.FromResult(new ProcessorResult(stripped, new List<InjectionWarning>()));
    }
}