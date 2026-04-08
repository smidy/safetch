using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        // 2. Remove data-* and on* attributes from every node
        var allNodes = doc.DocumentNode.SelectNodes("//*");
        if (allNodes != null)
        {
            foreach (var node in allNodes)
            {
                var attributes = node.Attributes.ToList();
                foreach (var attr in attributes)
                {
                    if (attr.Name.StartsWith("data-") || attr.Name.StartsWith("on"))
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

        // 5. Remove <script> and <style> elements
        var scriptStyleNodes = doc.DocumentNode.SelectNodes("//script|//style");
        if (scriptStyleNodes != null)
        {
            foreach (var node in scriptStyleNodes.ToList())
            {
                node.Remove();
            }
        }

        var resultHtml = doc.DocumentNode.OuterHtml;
        return Task.FromResult(new ProcessorResult(resultHtml, new List<InjectionWarning>()));
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
        var literalPatterns = new[]
        {
            "opacity:0",
            "display:none",
            "visibility:hidden",
            "color:white",
            "color:#fff",
            "color:#ffffff",
            "width:0",
            "height:0",
            "font-size:0",
        };
        if (literalPatterns.Any(p => normalizedStyle.Contains(p)))
            return true;

        if (IsInvisibleColor(normalizedStyle))
            return true;

        if (IsOffScreen(normalizedStyle))
            return true;

        return false;
    }

    // Detects rgb(255,255,255) and rgba(...,0) colour values that NormalizeStyle
    // may preserve with internal spaces (e.g. "color:rgba(255, 255, 255, 0)").
    private static bool IsInvisibleColor(string normalizedStyle)
    {
        var colorIdx = normalizedStyle.IndexOf("color:", StringComparison.Ordinal);
        if (colorIdx < 0) return false;

        var valueStart = colorIdx + 6;
        var semiIdx = normalizedStyle.IndexOf(';', valueStart);
        var colorValue = semiIdx >= 0
            ? normalizedStyle.Substring(valueStart, semiIdx - valueStart)
            : normalizedStyle.Substring(valueStart);

        // Collapse internal spaces for uniform comparison
        var compact = colorValue.Replace(" ", "");

        if (compact == "rgb(255,255,255)") return true;

        // rgba(r,g,b,0) or rgba(r,g,b,0.0) — any colour with zero alpha is invisible
        return Regex.IsMatch(compact, @"^rgba\(\d+,\d+,\d+,0(?:\.0*)?\)$");
    }

    // Detects position:absolute or position:fixed combined with a large negative
    // left or top offset (100px or more) — the classic "off-screen hiding" pattern.
    private static bool IsOffScreen(string normalizedStyle)
    {
        if (!normalizedStyle.Contains("position:absolute") &&
            !normalizedStyle.Contains("position:fixed"))
            return false;

        // Match ";left:-NNNpx" or ";top:-NNNpx" (3+ digit magnitude = ≥100px off-screen)
        return Regex.IsMatch(normalizedStyle, @"(?:^|;)(?:left|top):-\d{3,}");
    }
}