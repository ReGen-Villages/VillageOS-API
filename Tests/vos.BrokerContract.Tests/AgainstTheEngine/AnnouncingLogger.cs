using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using vos.Tests.Shared;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A hosted service that follows a stream says in its log when the subscription is open and its membership
// in place, and nothing else it exposes says so. A case waits for that line before it writes the change it
// expects to arrive, because a change written before the subscription opened is not owed to it.
public sealed class AnnouncingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = new();

    public Task UntilLoggedAsync(string fragment) =>
        Settle.UntilAsync(() => { lock (_lines) return _lines.Any(line => line.Contains(fragment, StringComparison.Ordinal)); },
            $"the service logs '{fragment}'");

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lines) _lines.Add(formatter(state, exception));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}

// Named anything but Testing, under which every service's subscription stays closed.
public sealed class DevelopmentEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "vos.BrokerContract.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
