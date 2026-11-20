using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The Pipelines page clears a wait box's limit by writing it empty, which only the engine can show is read back as no limit.
public class PhloemReadsAWaitLimitWrittenEmptyTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsAWaitLimitWrittenEmptyTests(TheEngine engine) => _engine = engine;

    private async Task SaveTheBoxAsync(Guid box, IReadOnlyDictionary<string, object> properties)
    {
        var saved = await _engine.Admin.PostAsJsonAsync("/api/model/fragment", new
        {
            Name = "A review",
            Things = new[] { new { Id = box, Name = "Wait for a review", Properties = properties } },
            Relationships = Array.Empty<object>(),
        });
        saved.EnsureSuccessStatusCode();
    }

    private async Task<string?> LimitReadByTheOrchestratorAsync(Guid box)
    {
        var read = await _engine.Admin.PostAsJsonAsync("/api/snapshots", new { ids = new[] { box }, includeIsAncestors = false });
        read.EnsureSuccessStatusCode();
        return SnapshotParser.Parse(await read.Content.ReadFromJsonAsync<JsonElement>()).Thing(box)!.PropertyString(ModelNames.WaitsAtMostMinutes);
    }

    private static object Limit(long? minutes) => new { typeInfo = "vos.Integer", value = minutes };

    [FactAgainstTheEngine]
    public async Task A_limit_left_out_of_a_later_save_stays_and_one_written_empty_reads_as_none()
    {
        var box = Guid.NewGuid();
        var position = new { typeInfo = "vos.Double", value = 10.0 };
        await SaveTheBoxAsync(box, new Dictionary<string, object> { ["x"] = position, [ModelNames.WaitsAtMostMinutes] = Limit(90) });
        (await LimitReadByTheOrchestratorAsync(box)).Should().Be("90");

        await SaveTheBoxAsync(box, new Dictionary<string, object> { ["x"] = position });
        (await LimitReadByTheOrchestratorAsync(box)).Should().Be("90", "a fragment keeps a value it leaves out");

        await SaveTheBoxAsync(box, new Dictionary<string, object> { ["x"] = position, [ModelNames.WaitsAtMostMinutes] = Limit(null) });
        (await LimitReadByTheOrchestratorAsync(box)).Should().BeNullOrWhiteSpace();
    }
}
