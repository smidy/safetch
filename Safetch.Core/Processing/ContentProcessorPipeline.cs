using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Processing;

public class ContentProcessorPipeline
{
    private readonly IEnumerable<OrderedProcessor> _processors;

    public ContentProcessorPipeline(IEnumerable<OrderedProcessor> processors)
    {
        _processors = processors;
    }

    public async Task<ProcessorResult> RunAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var warnings = new List<string>();
        var currentContent = content;

        // Sort by Order
        var sorted = _processors.OrderBy(p => p.Order);

        foreach (var ordered in sorted)
        {
            // Check affinity
            if (ordered.ContentTypeAffinity != "*" && ordered.ContentTypeAffinity != ctx.MimeType)
                continue;

            var result = await ordered.Processor.ProcessAsync(currentContent, ctx, ct);
            currentContent = result.Content;
            warnings.AddRange(result.Warnings);
        }

        return new ProcessorResult(currentContent, warnings);
    }
}