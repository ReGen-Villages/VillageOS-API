using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Covers `rename <thing> <new-name>` (US #5862): rename a Thing in place via the broker's
// PUT /api/things/{id}/name, keeping its Id and edges.
public class RenameCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _mycelium = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private static readonly Guid ThingId = Guid.NewGuid();

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;
    private Task Execute(string arg) => new RenameCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    [Fact]
    public async Task Rename_by_id_calls_the_broker_and_reports_success()
    {
        _mycelium.Setup(m => m.RenameThingAsync(ThingId, "NewName")).ReturnsAsync(true);

        await Execute($"{ThingId} NewName");

        _mycelium.Verify(m => m.RenameThingAsync(ThingId, "NewName"), Times.Once);
        _writer.ToString().Should().Contain("Renamed");
    }

    [Fact]
    public async Task Rename_resolves_the_target_by_name()
    {
        _mycelium.Setup(m => m.GetAllThingsAsync())
            .ReturnsAsync(Json($"[{{\"Id\":\"{ThingId}\",\"Name\":\"Pump\"}}]"));
        _mycelium.Setup(m => m.RenameThingAsync(ThingId, "Pump-7")).ReturnsAsync(true);

        await Execute("Pump Pump-7");

        _mycelium.Verify(m => m.RenameThingAsync(ThingId, "Pump-7"), Times.Once);
    }

    [Fact]
    public async Task New_name_may_contain_spaces()
    {
        _mycelium.Setup(m => m.RenameThingAsync(ThingId, "North Water Pump")).ReturnsAsync(true);

        await Execute($"{ThingId} North Water Pump");

        _mycelium.Verify(m => m.RenameThingAsync(ThingId, "North Water Pump"), Times.Once);
    }

    [Fact]
    public async Task Missing_new_name_shows_usage_and_does_not_call_the_broker()
    {
        await Execute($"{ThingId}");

        _writer.ToString().Should().Contain("Usage");
        _mycelium.Verify(m => m.RenameThingAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Broker_failure_is_reported_as_an_error()
    {
        _mycelium.Setup(m => m.RenameThingAsync(ThingId, "X")).ReturnsAsync(false);

        await Execute($"{ThingId} X");

        _writer.ToString().Should().Contain("Error");
    }

    [Fact]
    public async Task Unresolvable_target_reports_an_error_and_does_not_rename()
    {
        _mycelium.Setup(m => m.GetAllThingsAsync()).ReturnsAsync(Json("[]"));

        await Execute("NoSuchThing Whatever");

        _writer.ToString().Should().Contain("Error");
        _mycelium.Verify(m => m.RenameThingAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }
}
