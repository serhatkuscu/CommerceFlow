using CommerceFlow.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace CommerceFlow.Api.Tests;

public class CorrelationIdMiddlewareTests
{
    private const string HeaderName = "X-Correlation-ID";

    [Fact]
    public async Task NoHeader_GeneratesNewGuidCorrelationId()
    {
        var (context, logger) = await InvokeAsync(headerValue: null);

        Assert.True(Guid.TryParse(context.Response.Headers[HeaderName].ToString(), out _));
        Assert.Empty(logger.InformationLogs); // no header sent -- not a rejection, nothing to log
    }

    [Fact]
    public async Task ValidHeader_IsPreserved()
    {
        const string valid = "abc123-XYZ_789";

        var (context, logger) = await InvokeAsync(valid);

        Assert.Equal(valid, context.Response.Headers[HeaderName].ToString());
        Assert.Empty(logger.InformationLogs);
    }

    [Fact]
    public async Task InvalidCharacters_AreReplacedWithGeneratedId()
    {
        var (context, logger) = await InvokeAsync("bad value! with spaces");

        Assert.True(Guid.TryParse(context.Response.Headers[HeaderName].ToString(), out _));
        Assert.Single(logger.InformationLogs);
        Assert.Contains("Reason=InvalidCharacters", logger.InformationLogs[0]);
    }

    [Fact]
    public async Task TooLong_IsReplacedWithGeneratedId()
    {
        var tooLong = new string('a', 65);

        var (context, logger) = await InvokeAsync(tooLong);

        Assert.True(Guid.TryParse(context.Response.Headers[HeaderName].ToString(), out _));
        Assert.Single(logger.InformationLogs);
        Assert.Contains("Reason=TooLong", logger.InformationLogs[0]);
        Assert.Contains("ProvidedLength=65", logger.InformationLogs[0]);
    }

    [Fact]
    public async Task MultipleHeaderValues_AreReplacedWithGeneratedId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = new StringValues(["id-one", "id-two"]);
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        Assert.True(Guid.TryParse(context.Response.Headers[HeaderName].ToString(), out _));
        Assert.Single(logger.InformationLogs);
        Assert.Contains("Reason=MultipleValues", logger.InformationLogs[0]);
        Assert.Contains("ValueCount=2", logger.InformationLogs[0]);
    }

    [Fact]
    public async Task UsedCorrelationId_IsEchoedInResponseHeader()
    {
        const string valid = "known-id-123";

        var (context, _) = await InvokeAsync(valid);

        Assert.Equal(valid, context.Response.Headers[HeaderName].ToString());
    }

    // Proves propagation via a stable mechanism -- intercepting the actual ILogger.BeginScope
    // call while `next` runs -- rather than parsing console text, which would be fragile against
    // formatting changes and tells you nothing about *when* the scope was active.
    [Fact]
    public async Task CorrelationId_IsActiveInLogScopeWhileNextRuns()
    {
        const string valid = "scope-check-id";
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = valid;
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        string? scopedIdDuringNext = null;

        var middleware = new CorrelationIdMiddleware(_ =>
        {
            scopedIdDuringNext = logger.CurrentScopeCorrelationId;
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(valid, scopedIdDuringNext);
    }

    private static async Task<(DefaultHttpContext Context, CapturingLogger<CorrelationIdMiddleware> Logger)> InvokeAsync(
        string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers[HeaderName] = headerValue;
        }

        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        return (context, logger);
    }
}
