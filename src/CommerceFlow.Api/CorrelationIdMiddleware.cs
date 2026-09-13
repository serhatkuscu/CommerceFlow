using System.Text.RegularExpressions;

namespace CommerceFlow.Api;

// HTTP-specific by design -- lives in Api, not Application/Infrastructure/Domain, which must
// never know X-Correlation-ID, HttpContext, or Serilog exist. Runs early in the pipeline (before
// central exception handling and Serilog request logging) so both see the correlation ID.
// Never throws: any input that fails validation just falls back to a fresh generated ID rather
// than failing the request -- correlation ID is observability metadata, not a business concern.
public sealed partial class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context, out var rejection);

        if (rejection is not null)
        {
            // Structured fields only -- never the raw client-supplied value, never a hash of it.
            // Reason + length/count is enough to understand why without ever writing
            // attacker-controlled content into the logs.
            _logger.LogInformation(
                "Client correlation ID rejected. Reason={Reason} ProvidedLength={ProvidedLength} ValueCount={ValueCount} CorrelationId={CorrelationId}",
                rejection.Reason, rejection.ProvidedLength, rejection.ValueCount, correlationId);
        }

        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context, out RejectionInfo? rejection)
    {
        rejection = null;

        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Guid.NewGuid().ToString();
        }

        if (values.Count > 1)
        {
            rejection = new RejectionInfo(RejectionReason.MultipleValues, ProvidedLength: null, values.Count);
            return Guid.NewGuid().ToString();
        }

        var value = values[0];

        if (string.IsNullOrEmpty(value))
        {
            rejection = new RejectionInfo(RejectionReason.Empty, ProvidedLength: 0, ValueCount: null);
            return Guid.NewGuid().ToString();
        }

        if (value.Length > MaxLength)
        {
            rejection = new RejectionInfo(RejectionReason.TooLong, value.Length, ValueCount: null);
            return Guid.NewGuid().ToString();
        }

        if (!ValidCharacters().IsMatch(value))
        {
            rejection = new RejectionInfo(RejectionReason.InvalidCharacters, value.Length, ValueCount: null);
            return Guid.NewGuid().ToString();
        }

        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ValidCharacters();

    private enum RejectionReason
    {
        Empty,
        TooLong,
        InvalidCharacters,
        MultipleValues
    }

    private sealed record RejectionInfo(RejectionReason Reason, int? ProvidedLength, int? ValueCount);
}
