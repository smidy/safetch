namespace Safetch.Core.Processing;

public class HtmlToMarkdownProcessor : IContentProcessor
{
    public string Name => "HtmlToMarkdown";

    public Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct)
    {
        var config = new ReverseMarkdown.Config
        {
            UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
            RemoveComments = true,
            SmartHrefHandling = true,
        };
        var converter = new ReverseMarkdown.Converter(config);
        var markdown = converter.Convert(content);
        return Task.FromResult(new ProcessorResult(markdown, new List<string>(), new List<InjectionWarning>()));
    }
}