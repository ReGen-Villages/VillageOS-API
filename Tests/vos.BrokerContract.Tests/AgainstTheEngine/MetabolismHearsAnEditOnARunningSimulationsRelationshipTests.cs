using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Metabolism keeps a simulation current by streaming property changes on its relationship. Its unit tests
// record what the service asks the subscription for, so only the engine can say whether what it asks for
// is a membership the relationship's changes travel through.
public class MetabolismHearsAnEditOnARunningSimulationsRelationshipTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public MetabolismHearsAnEditOnARunningSimulationsRelationshipTests(TheEngine engine) => _engine = engine;

    private sealed class RecordingMetabolism : Metabolism
    {
        public RecordingMetabolism(TheEngine engine)
            : base(new MyceliumClient(engine.ClientFactory, NullLogger<MyceliumClient>.Instance, TheEngine.Url,
                       ResourceDirection.Consumes, new LaunchToken(engine.AdminToken)),
                   NullLogger<Metabolism>.Instance, ResourceDirection.Consumes) { }

        public ConcurrentQueue<(string Relationship, string Property)> Heard { get; } = new();

        public override void UpdateProperty(string relationshipId, string propertyName, object? newValue)
            => Heard.Enqueue((relationshipId, propertyName));
    }

    [FactAgainstTheEngine]
    public async Task An_edit_on_the_relationships_quantity_reaches_the_engine()
    {
        var consumes = await _engine.DeclareOnceAsync("consumes");
        var building = await _engine.DeclareAsync($"Building {Guid.NewGuid():N}");
        var reservoir = await _engine.DeclareAsync($"Reservoir {Guid.NewGuid():N}");
        var relationshipId = await RelateAsync(building, consumes, reservoir);
        await SetQuantityAsync(relationshipId, 5);

        var engine = new RecordingMetabolism(_engine);
        var log = new AnnouncingLogger<MetabolismSubscriptionService>();
        var service = new MetabolismSubscriptionService(
            new SubscriptionClient(_engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken)),
            engine, new DevelopmentEnvironment(), log);
        engine.Register(new SimulationConfig(
            relationshipId.ToString(), building.ToString(), reservoir.ToString(), "Building", 5m, "m3", "quantity", 60,
            DateTime.UtcNow, DateTime.UtcNow.AddYears(1), StartDelaySeconds: 99999m), HeldModelToken.None);
        await service.StartAsync(CancellationToken.None);
        await log.UntilLoggedAsync("subscribed to Mycelium");

        await SetQuantityAsync(relationshipId, 7);

        try
        {
            await Settle.UntilAsync(
                () => engine.Heard.Contains((relationshipId.ToString(), "quantity")),
                "the edit on the relationship reaches the engine");
        }
        finally
        {
            await engine.StopAllAsync();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private async Task<Guid> RelateAsync(Guid subjectId, Guid predicateId, Guid targetId)
    {
        var related = await _engine.Admin.PostAsJsonAsync("/api/relationships", new { subjectId, predicateId, targetId });
        related.EnsureSuccessStatusCode();
        return (await related.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetGuid();
    }

    private async Task SetQuantityAsync(Guid relationshipId, double quantity)
    {
        var set = await _engine.Admin.PutAsJsonAsync($"/api/relationships/{relationshipId}/properties",
            new { name = "quantity", type = "vos.Double", value = quantity });
        set.EnsureSuccessStatusCode();
    }
}
