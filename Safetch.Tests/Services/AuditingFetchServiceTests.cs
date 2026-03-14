using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Safetch.Core.Models;
using Safetch.Core.Processing;
using Safetch.Core.Services;
using Xunit;

namespace Safetch.Tests.Services;

public class AuditingFetchServiceTests
{
    private static (AuditingFetchService sut, Mock<IFetchService> inner, Mock<ILogger<AuditingFetchService>> logger)
        CreateSut()
    {
        var inner = new Mock<IFetchService>();
        var logger = new Mock<ILogger<AuditingFetchService>>();
        var sut = new AuditingFetchService(inner.Object, logger.Object);
        return (sut, inner, logger);
    }

    private static FetchRequest MakeRequest(string? sessionId = "test-session")
        => new FetchRequest { Url = "https://example.com", SessionId = sessionId };

    private static FetchResponse MakeSuccess(InjectionWarning[]? injectionWarnings = null)
        => new FetchResponse
        {
            Success = true,
            Url = "https://example.com",
            Content = "Hello world",
            StatusCode = 200,
            ContentType = "text/html",
            ContentBytes = 11,
            InjectionWarnings = injectionWarnings ?? Array.Empty<InjectionWarning>()
        };

    private static FetchResponse MakeBlocked()
        => new FetchResponse { Success = false, ErrorCode = "BLOCKED", ErrorMessage = "SSRF blocked" };

    // Helper to verify a specific log level was called at least once
    private static void VerifyLogged(Mock<ILogger<AuditingFetchService>> logger, LogLevel level)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task FetchAsync_AlwaysLogsStarted()
    {
        var (sut, inner, logger) = CreateSut();
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeSuccess());

        await sut.FetchAsync(MakeRequest(), CancellationToken.None);

        VerifyLogged(logger, LogLevel.Information);
    }

    [Fact]
    public async Task FetchAsync_SuccessResponse_LogsCompleted()
    {
        var (sut, inner, logger) = CreateSut();
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeSuccess());

        await sut.FetchAsync(MakeRequest(), CancellationToken.None);

        // At least 2 Info logs: fetch.started + fetch.completed
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task FetchAsync_BlockedResponse_LogsWarning()
    {
        var (sut, inner, logger) = CreateSut();
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeBlocked());

        await sut.FetchAsync(MakeRequest(), CancellationToken.None);

        VerifyLogged(logger, LogLevel.Warning);
    }

    [Fact]
    public async Task FetchAsync_SuccessWithInjectionWarnings_LogsContentWarningPerWarning()
    {
        var (sut, inner, logger) = CreateSut();
        var warnings = new[]
        {
            new InjectionWarning("InstructionOverride", "ignore previous", InjectionSeverity.Medium),
            new InjectionWarning("PersonaHijacking", "you are now", InjectionSeverity.High)
        };
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeSuccess(injectionWarnings: warnings));

        await sut.FetchAsync(MakeRequest(), CancellationToken.None);

        // 2 injection warnings → 2 Warning-level logs
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task FetchAsync_InnerThrows_LogsErrorAndRethrows()
    {
        var (sut, inner, logger) = CreateSut();
        var ex = new InvalidOperationException("boom");
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(ex);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.FetchAsync(MakeRequest(), CancellationToken.None));

        VerifyLogged(logger, LogLevel.Error);
    }

    [Fact]
    public async Task FetchAsync_ReturnsInnerResponse()
    {
        var (sut, inner, _) = CreateSut();
        var expected = MakeSuccess();
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);

        var result = await sut.FetchAsync(MakeRequest(), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("")]
    public async Task FetchAsync_MalformedUrl_DoesNotThrow(string? url)
    {
        var (sut, inner, _) = CreateSut();
        inner.Setup(s => s.FetchAsync(It.IsAny<FetchRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeSuccess());

        // Should not throw — GetHost handles null/malformed gracefully
        var result = await sut.FetchAsync(new FetchRequest { Url = url, SessionId = "s" }, CancellationToken.None);
        Assert.NotNull(result);
    }
}