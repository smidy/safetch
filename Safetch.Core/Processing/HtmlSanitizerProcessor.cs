using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Safetch.Core.Processing;

public class HtmlSanitizerProcessor : IContentProcessor
{
    public string Name => "HtmlSanitizer";

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        // 1. Remove CSS-invisible elements
        var nodesWithStyle = doc.DocumentNode.SelectNodes("//*[@style]");
        if (nodesWithStyle != null)
        {
            foreach (var node in nodesWithStyle.ToList()) // ToList to avoid collection modification issues
            {
                var style = node.GetAttributeValue("style", "");
                var normalized = NormalizeStyle(style);
                if (IsInvisibleStyle(normalized))
                {
                    node.Remove();
                }
            }
        }

        // 2. Remove data-* attributes from every node
        var allNodes = doc.DocumentNode.SelectNodes("//*");
        if (allNodes != null)
        {
            foreach (var node in allNodes)
            {
                var attributes = node.Attributes.ToList();
                foreach (var attr in attributes)
                {
                    if (attr.Name.StartsWith("data-"))
                    {
                        node.Attributes.Remove(attr);
                    }
                }
            }
        }

        // 3. Remove <svg> elements
        var svgNodes = doc.DocumentNode.SelectNodes("//svg");
        if (svgNodes != null)
        {
            foreach (var svg in svgNodes.ToList())
            {
                svg.Remove();
            }
        }

        // 4. Remove <meta http-equiv> elements
        var metaNodes = doc.DocumentNode.SelectNodes("//meta[@http-equiv]");
        if (metaNodes != null)
        {
            foreach (var meta in metaNodes.ToList())
            {
                meta.Remove();
            }
        }

        var resultHtml = doc.DocumentNode.OuterHtml;
        return Task.FromResult(new ProcessorResult(resultHtml, new List<string>()));
    }

    private static string NormalizeStyle(string style)
    {
        // Remove spaces around colons and semicolons, then lowercase
        // Replace ": " with ":" and " ;" with ";" and " ; " etc.
        // Simple approach: split by ';', trim each part, split by ':', trim, join back.
        var parts = style.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
        var normalizedParts = new List<string>();
        foreach (var part in parts)
        {
            var subparts = part.Split(':', 2);
            if (subparts.Length == 2)
            {
                var property = subparts[0].Trim().ToLowerInvariant();
                var value = subparts[1].Trim().ToLowerInvariant();
                normalizedParts.Add($"{property}:{value}");
            }
        }
        return string.Join(";", normalizedParts);
    }

    private static bool IsInvisibleStyle(string normalizedStyle)
    {
        // Check for substrings
        var invisiblePatterns = new[]
        {
            "opacity:0",
            "display:none",
            "visibility:hidden",
            "color:white",
            "color:#fff",
            "color:#ffffff",
            "width:0",
            "height:0",
            "font-size:0"
        };
        return invisiblePatterns.Any(pattern => normalizedStyle.Contains(pattern));
    }
}