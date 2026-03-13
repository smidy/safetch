using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Safetch.Core.Processing;
using Xunit;
using EmptyInjections = System.Collections.Generic.List<Safetch.Core.Processing.InjectionWarning>;

namespace Safetch.Tests.Processing;

public class ContentProcessorPipelineTests
{
    [Fact]
    public async Task HtmlProcessorsSkipNonHtmlContent()
    {
        var mockProcessor = new Mock<IContentProcessor>();
        mockProcessor.Setup(p => p.Name).Returns("Test");
        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ProcessorResult("processed", new List<string>(), new EmptyInjections()));
        
        var processors = new List<OrderedProcessor>
        {
            new OrderedProcessor(1, "text/html", mockProcessor.Object)
        };
        var pipeline = new ContentProcessorPipeline(processors);
        
        var result = await pipeline.RunAsync("content", new ProcessingContext("text/plain", "http://example.com"), default);
        
        // Processor should not have been called
        mockProcessor.Verify(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        Assert.Equal("content", result.Content);
    }

    [Fact]
    public async Task StarProcessorsRunForAllContentTypes()
    {
        var callCount = 0;
        var mockProcessor = new Mock<IContentProcessor>();
        mockProcessor.Setup(p => p.Name).Returns("Star");
        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(new ProcessorResult("processed", new List<string>(), new EmptyInjections()));
        
        var processors = new List<OrderedProcessor>
        {
            new OrderedProcessor(1, "*", mockProcessor.Object)
        };
        var pipeline = new ContentProcessorPipeline(processors);
        
        // Run with different content types
        await pipeline.RunAsync("content", new ProcessingContext("text/html", "http://example.com"), default);
        await pipeline.RunAsync("content", new ProcessingContext("text/plain", "http://example.com"), default);
        await pipeline.RunAsync("content", new ProcessingContext("application/octet-stream", "http://example.com"), default);
        
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task UnrecognisedContentTypeRunsOnlyStarProcessors()
    {
        var starCalled = false;
        var htmlCalled = false;
        
        var starProcessor = new Mock<IContentProcessor>();
        starProcessor.Setup(p => p.Name).Returns("Star");
        starProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => starCalled = true)
            .ReturnsAsync(new ProcessorResult("star", new List<string>(), new EmptyInjections()));
        
        var htmlProcessor = new Mock<IContentProcessor>();
        htmlProcessor.Setup(p => p.Name).Returns("Html");
        htmlProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => htmlCalled = true)
            .ReturnsAsync(new ProcessorResult("html", new List<string>(), new EmptyInjections()));
        
        var processors = new List<OrderedProcessor>
        {
            new OrderedProcessor(1, "text/html", htmlProcessor.Object),
            new OrderedProcessor(2, "*", starProcessor.Object)
        };
        var pipeline = new ContentProcessorPipeline(processors);
        
        var result = await pipeline.RunAsync("content", new ProcessingContext("application/octet-stream", "http://example.com"), default);
        
        Assert.False(htmlCalled);
        Assert.True(starCalled);
        Assert.Equal("star", result.Content);
    }

    [Fact]
    public async Task WarningsAccumulateAcrossProcessors()
    {
        var processor1 = new Mock<IContentProcessor>();
        processor1.Setup(p => p.Name).Returns("P1");
        processor1.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ProcessorResult("p1", new List<string> { "warning1" }, new EmptyInjections()));
        
        var processor2 = new Mock<IContentProcessor>();
        processor2.Setup(p => p.Name).Returns("P2");
        processor2.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ProcessorResult("p2", new List<string> { "warning2" }, new EmptyInjections()));
        
        var processors = new List<OrderedProcessor>
        {
            new OrderedProcessor(1, "*", processor1.Object),
            new OrderedProcessor(2, "*", processor2.Object)
        };
        var pipeline = new ContentProcessorPipeline(processors);
        
        var result = await pipeline.RunAsync("content", new ProcessingContext("text/plain", "http://example.com"), default);
        
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("warning1", result.Warnings);
        Assert.Contains("warning2", result.Warnings);
    }

    [Fact]
    public async Task ProcessorsRunInRegisteredOrder()
    {
        var executionOrder = new List<string>();
        
        var processor1 = new Mock<IContentProcessor>();
        processor1.Setup(p => p.Name).Returns("P1");
        processor1.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => executionOrder.Add("P1"))
            .ReturnsAsync(new ProcessorResult("p1", new List<string>(), new EmptyInjections()));
        
        var processor2 = new Mock<IContentProcessor>();
        processor2.Setup(p => p.Name).Returns("P2");
        processor2.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingContext>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback(() => executionOrder.Add("P2"))
            .ReturnsAsync(new ProcessorResult("p2", new List<string>(), new EmptyInjections()));
        
        var processors = new List<OrderedProcessor>
        {
            new OrderedProcessor(5, "*", processor2.Object),
            new OrderedProcessor(1, "*", processor1.Object)
        };
        var pipeline = new ContentProcessorPipeline(processors);
        
        await pipeline.RunAsync("content", new ProcessingContext("text/plain", "http://example.com"), default);
        
        Assert.Equal(new[] { "P1", "P2" }, executionOrder);
    }
}