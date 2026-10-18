using System.Text.Json;
using CommerceFlow.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CommerceFlow.Api.Tests;

// Deliberately no test-only throwing endpoint added to CommerceFlow.Api -- GlobalExceptionHandler
// is tested directly (constructed by hand, invoked with a real Exception), the same way
// CorrelationIdMiddlewareTests tests the middleware directly. IProblemDetailsService comes from a
// real WebApplicationFactory host, so the ProblemDetails JSON contract is the actual one the app
// would use, not a hand-rolled approximation. The DI-registration test proves the handler is
// actually wired into the real Program.cs pipeline, not just that the class works in isolation.
public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnhandledException_Returns500()
    {
        var (context, _, handled) = await InvokeAsync(new InvalidOperationException("boom"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_ContentTypeIsProblemJson()
    {
        var (context, _, _) = await InvokeAsync(new InvalidOperationException("boom"));

        Assert.StartsWith("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task UnhandledException_DoesNotLeakExceptionMessageOrType()
    {
        const string secretMessage = "database password is hunter2";
        var (context, _, _) = await InvokeAsync(new InvalidOperationException(secretMessage));

        var body = await ReadBodyAsync(context);

        Assert.DoesNotContain(secretMessage, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnhandledException_ProblemDetailsContainsCorrelationId()
    {
        const string correlationId = "known-correlation-id";
        var (context, _, _) = await InvokeAsync(new InvalidOperationException("boom"), correlationId);

        var body = await ReadBodyAsync(context);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task UnhandledException_BodyCorrelationIdMatchesResponseHeader()
    {
        const string correlationId = "matching-id-test";
        var (context, _, _) = await InvokeAsync(new InvalidOperationException("boom"), correlationId);

        var body = await ReadBodyAsync(context);
        using var json = JsonDocument.Parse(body);
        var bodyCorrelationId = json.RootElement.GetProperty("correlationId").GetString();

        Assert.Equal(bodyCorrelationId, context.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Fact]
    public async Task UnhandledException_IsLoggedAtErrorLevelExactlyOnce()
    {
        var (_, logger, _) = await InvokeAsync(new InvalidOperationException("boom"));

        Assert.Single(logger.ErrorLogs);
    }

    [Fact]
    public async Task GlobalExceptionHandler_IsRegisteredInTheRealPipeline()
    {
        await using var factory = new WebApplicationFactory<Program>();

        var handlers = factory.Services.GetServices<IExceptionHandler>();

        Assert.Contains(handlers, h => h is GlobalExceptionHandler);
    }

    private static async Task<(DefaultHttpContext Context, CapturingLogger<GlobalExceptionHandler> Logger, bool Handled)> InvokeAsync(
        Exception exception, string correlationId = "test-correlation-id")
    {
        await using var factory = new WebApplicationFactory<Program>();
        var problemDetailsService = factory.Services.GetRequiredService<IProblemDetailsService>();

        var context = new DefaultHttpContext { RequestServices = factory.Services };
        context.Items[CorrelationIdMiddleware.CorrelationIdItemKey] = correlationId;
        context.Response.Body = new MemoryStream();

        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(problemDetailsService, logger);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        return (context, logger, handled);
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
