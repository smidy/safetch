namespace Safetch.Core.Processing;

public record OrderedProcessor(int Order, string ContentTypeAffinity, IContentProcessor Processor);