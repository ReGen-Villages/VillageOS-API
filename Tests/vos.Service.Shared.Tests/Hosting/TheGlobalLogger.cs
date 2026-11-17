using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

// Serilog's Log.Logger is one per process, so tests that replace it must not run beside each other.
[CollectionDefinition(Name)]
public sealed class TheGlobalLogger
{
    public const string Name = "Tests that replace the global Serilog logger";
}
