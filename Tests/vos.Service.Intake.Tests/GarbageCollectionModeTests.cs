using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class GarbageCollectionModeTests
{
    [Fact]
    public void The_service_runs_workstation_garbage_collection_so_its_heap_does_not_grow_with_the_hosts_core_count()
    {
        var builtConfiguration = Path.Combine(AppContext.BaseDirectory, "vos.Service.Intake.runtimeconfig.json");
        using var configuration = JsonDocument.Parse(File.ReadAllText(builtConfiguration));

        configuration.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties")
            .TryGetProperty("System.GC.Server", out var serverCollection).Should().BeTrue();
        serverCollection.GetBoolean().Should().BeFalse();
    }
}
