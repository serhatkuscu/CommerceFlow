using Microsoft.Extensions.Logging;

namespace CommerceFlow.Api.Tests;

// Minimal ILogger test double: captures BeginScope's CorrelationId (as a stack, mirroring real
// ambient-scope nesting) and the formatted text of every log call, tagged by level. Deliberately
// not a mocking framework -- there's exactly one interface to implement and the behavior needs to
// be exactly right, not generic. Shared by CorrelationIdMiddlewareTests and
// GlobalExceptionHandlerTests rather than duplicated.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Stack<string?> _scopeCorrelationIds = new();
    private readonly List<(LogLevel Level, string Message)> _logs = [];

    public IReadOnlyList<string> InformationLogs =>
        _logs.Where(l => l.Level == LogLevel.Information).Select(l => l.Message).ToList();

    public IReadOnlyList<string> ErrorLogs =>
        _logs.Where(l => l.Level == LogLevel.Error).Select(l => l.Message).ToList();

    public string? CurrentScopeCorrelationId => _scopeCorrelationIds.Count > 0 ? _scopeCorrelationIds.Peek() : null;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        string? correlationId = null;
        if (state is IEnumerable<KeyValuePair<string, object>> properties)
        {
            correlationId = properties.FirstOrDefault(p => p.Key == "CorrelationId").Value?.ToString();
        }

        _scopeCorrelationIds.Push(correlationId);
        return new PopScopeOnDispose(_scopeCorrelationIds);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logs.Add((logLevel, formatter(state, exception)));
    }

    private sealed class PopScopeOnDispose(Stack<string?> scopes) : IDisposable
    {
        public void Dispose() => scopes.Pop();
    }
}
