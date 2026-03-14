using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Safetch.Core.Models;

namespace Safetch.Core.Processing;

/// <summary>
/// Extracts the primary article content from HTML using Mozilla Readability (via SmartReader).
/// Only active when FetchRequest.Mode is Readable or Text — communicated via ProcessingContext.
/// </summary>
public class ReadableContentProcessor : IContentProcessor
{
    public string Name => "ReadableContent";

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        bool wantText = ctx.MimeType == "text/html+text";
        content = content.Replace("\r\n", "")
                     .Replace("\n", "")
                     .Replace("\r", "");
        var article = SmartReader.Reader.ParseArticle(ctx.SourceUrl, text: content);

        if (!article.IsReadable)
        {
            // Fallback: do basic tag strip for text mode, or return as-is
            var fallback = wantText ? StripTags(content) : content;
            return Task.FromResult(new ProcessorResult(fallback, new List<InjectionWarning>()));
        }

        var extracted = wantText ? StripTags(article.Content) : article.Content;
        return Task.FromResult(new ProcessorResult(extracted, new List<InjectionWarning>()));
    }

    private static string StripTags(string html)
        => Regex.Replace(html ?? string.Empty, "<[^>]+>", " ")
                .Replace("&nbsp;", " ")
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"");
}