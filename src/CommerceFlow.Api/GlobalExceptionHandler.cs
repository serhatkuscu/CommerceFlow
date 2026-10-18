using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CommerceFlow.Api;

// The last line of defense for anything that reaches here uncaught -- not a place to encode
// business rules. There is no Domain/Application exception hierarchy yet (no real use cases
// exist), so every exception is treated the same: log the full detail server-side exactly once,
// return a generic RFC 7807 Problem Details response with nothing exception-specific leaked to
// the client. Deliberately simple -- no external calls, no branching on exception type -- so this
// handler itself has as little surface as possible to fail on.
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string ?? "unknown";

        // Full exception detail, server-side only, exactly once -- this is the only place in the
        // app that logs an unhandled exception. CorrelationId comes through automatically via the
        // BeginScope CorrelationIdMiddleware already opened around this entire request.
        _logger.LogError(exception, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

        // Re-asserted explicitly rather than trusted to have survived: ExceptionHandlerMiddleware
        // resets the response before calling this handler, so anything CorrelationIdMiddleware set
        // on the response earlier cannot be assumed to still be there.
        httpContext.Response.Headers["X-Correlation-ID"] = correlationId;
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "An unexpected error occurred while processing your request.",
                Extensions = { ["correlationId"] = correlationId }
            }
        });
    }
}
