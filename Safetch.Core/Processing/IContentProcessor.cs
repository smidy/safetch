namespace Safetch.Core.Processing;

public interface IContentProcessor
{
    string Name { get; }
    Task<ProcessorResult> ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct);
}

public record ProcessorResult(string Content, IReadOnlyList<string> Warnings);

/// <param name="MimeType">Parsed MIME type only — e.g. "text/html", not "text/html; charset=utf-8".</param>
/// <param name="SourceUrl">Originating URL for context.</param>
public record ProcessingContext(string MimeType, string SourceUrl);