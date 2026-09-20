using Microsoft.Extensions.Logging;

namespace vos.Tests.Shared;

// Keeps what a component logged, so a test can say what a line must and must not carry.
// Serilog owns a host's logging and drops providers added through ConfigureLogging, so a
// capturing provider never sees a line. Registering this as the closed ILogger<T> wins over the
// open generic Serilog registers, which is the way in.
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var line = formatter(state, exception);
        if (exception is not null) line += " " + exception;
        lock (_lines) _lines.Add(line);
    }
}
