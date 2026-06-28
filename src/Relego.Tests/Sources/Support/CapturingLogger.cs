using Microsoft.Extensions.Logging;

namespace Relego.Tests.Sources.Support;

/// <summary>Minimal <see cref="ILogger"/> that captures log entries for assertion.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
