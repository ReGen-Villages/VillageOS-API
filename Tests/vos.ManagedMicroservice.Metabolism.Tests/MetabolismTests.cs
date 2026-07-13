using System.Text.Json;
using vos.ManagedMicroservice.Metabolism.Models;
using vos.ManagedMicroservice.Metabolism.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

public class MetabolismTests
{
    private readonly Services.Metabolism _engine;

    public MetabolismTests()
    {
        // MyceliumClient needs IHttpClientFactory — mock it so simulation ticks fail harmlessly
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var myceliumLogger = new Mock<ILogger<MyceliumClient>>();
        var myceliumClient = new MyceliumClient(httpFactory.Object, myceliumLogger.Object, "http://localhost:0", "consumes");

        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        _engine = new Services.Metabolism(myceliumClient, engineLogger.Object, "consumes");
    }

    private SimulationConfig MakeConfig(string relId = "rel-1", decimal quantity = 5.0m, int freqSeconds = 60) =>
        new(relId, "subject-1", "target-1", "TestSubject", quantity, "kWh", "quantity", freqSeconds,
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2)); // future start so loop just waits

    #region UpdateProperty with native values

    [Fact]
    public void UpdateProperty_Quantity_UpdatesConfig()
    {
        _engine.Register(MakeConfig());

        _engine.UpdateProperty("rel-1", "quantity", 10.0m);

        var sim = _engine.GetAll().First();
        sim.Config.Quantity.Should().Be(10.0m);
    }

    [Fact]
    public void UpdateProperty_FrequencySeconds_UpdatesConfig()
    {
        _engine.Register(MakeConfig());

        _engine.UpdateProperty("rel-1", "frequencySeconds", 30);

        var sim = _engine.GetAll().First();
        sim.Config.FrequencySeconds.Should().Be(30);
    }

    [Fact]
    public void UpdateProperty_Unit_UpdatesConfig()
    {
        _engine.Register(MakeConfig());

        _engine.UpdateProperty("rel-1", "unit", "litres");

        var sim = _engine.GetAll().First();
        sim.Config.Unit.Should().Be("litres");
    }

    [Fact]
    public void UpdateProperty_PropertyPath_UpdatesConfig()
    {
        _engine.Register(MakeConfig());

        _engine.UpdateProperty("rel-1", "propertyPath", "daily_water_litres");

        var sim = _engine.GetAll().First();
        sim.Config.PropertyPath.Should().Be("daily_water_litres");
    }

    [Fact]
    public void UpdateProperty_UnknownProperty_DoesNotRestart()
    {
        _engine.Register(MakeConfig());
        var originalTicks = _engine.GetAll().First().TickCount;

        _engine.UpdateProperty("rel-1", "total_consumed", 999.0m);

        // Config unchanged
        var sim = _engine.GetAll().First();
        sim.Config.Quantity.Should().Be(5.0m);
    }

    [Fact]
    public void UpdateProperty_UnknownRelationship_DoesNothing()
    {
        _engine.Register(MakeConfig());

        // Should not throw
        _engine.UpdateProperty("nonexistent", "quantity", 10.0m);

        _engine.GetAll().Should().HaveCount(1);
    }

    #endregion

    #region UpdateProperty with JsonElement values (SSE scenario)

    [Fact]
    public void UpdateProperty_JsonElement_Double_UpdatesQuantity()
    {
        _engine.Register(MakeConfig());

        var json = JsonDocument.Parse("10.5").RootElement;
        _engine.UpdateProperty("rel-1", "quantity", json);

        _engine.GetAll().First().Config.Quantity.Should().Be(10.5m);
    }

    [Fact]
    public void UpdateProperty_JsonElement_Int_UpdatesFrequency()
    {
        _engine.Register(MakeConfig());

        var json = JsonDocument.Parse("15").RootElement;
        _engine.UpdateProperty("rel-1", "frequencySeconds", json);

        _engine.GetAll().First().Config.FrequencySeconds.Should().Be(15);
    }

    [Fact]
    public void UpdateProperty_JsonElement_String_UpdatesUnit()
    {
        _engine.Register(MakeConfig());

        var json = JsonDocument.Parse("\"litres\"").RootElement;
        _engine.UpdateProperty("rel-1", "unit", json);

        _engine.GetAll().First().Config.Unit.Should().Be("litres");
    }

    [Fact]
    public void UpdateProperty_JsonElement_InvalidType_DoesNotCrash()
    {
        _engine.Register(MakeConfig());

        var json = JsonDocument.Parse("\"not_a_number\"").RootElement;
        _engine.UpdateProperty("rel-1", "quantity", json);

        // Should remain unchanged — conversion fails gracefully
        _engine.GetAll().First().Config.Quantity.Should().Be(5.0m);
    }

    #endregion

    #region Register re-registration

    [Fact]
    public void Register_SameRelationship_CancelsOldSimulation()
    {
        var entry1 = _engine.Register(MakeConfig());
        entry1.Status.Should().Be("waiting"); // future start

        var entry2 = _engine.Register(MakeConfig(quantity: 20.0m));

        _engine.GetAll().Should().HaveCount(1);
        _engine.GetAll().First().Config.Quantity.Should().Be(20.0m);
        entry1.Cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void UpdateProperty_StartDelaySeconds_UpdatesConfig()
    {
        _engine.Register(MakeConfig());

        _engine.UpdateProperty("rel-1", "startDelaySeconds", 30.0m);

        var sim = _engine.GetAll().First();
        sim.Config.StartDelaySeconds.Should().Be(30.0m);
    }

    [Fact]
    public void UpdateProperty_JsonElement_Decimal_UpdatesStartDelaySeconds()
    {
        _engine.Register(MakeConfig());

        var json = JsonDocument.Parse("10.5").RootElement;
        _engine.UpdateProperty("rel-1", "startDelaySeconds", json);

        _engine.GetAll().First().Config.StartDelaySeconds.Should().Be(10.5m);
    }

    #endregion

    #region Concurrent updates

    [Fact]
    public void UpdateProperty_ConcurrentQuantityAndFrequency_BothApplied()
    {
        _engine.Register(MakeConfig(quantity: 1.0m, freqSeconds: 60));

        // Simulate two rapid SSE callbacks on thread pool
        var barrier = new Barrier(2);
        var t1 = Task.Run(() => { barrier.SignalAndWait(); _engine.UpdateProperty("rel-1", "quantity", 3.0m); });
        var t2 = Task.Run(() => { barrier.SignalAndWait(); _engine.UpdateProperty("rel-1", "frequencySeconds", 5); });
        Task.WaitAll(t1, t2);

        var config = _engine.GetAll().First().Config;
        config.Quantity.Should().Be(3.0m);
        config.FrequencySeconds.Should().Be(5);
    }

    #endregion

    #region RunSimulationLoop execution tests

    // Create a Metabolism engine whose MyceliumClient is backed by a MockHttpMessageHandler
    // so that ApplyQuantityAsync and IncrementRelationshipPropertyAsync succeed.
    private static Services.Metabolism CreateEngineWithMockedMycelium(
        Func<HttpRequestMessage, HttpResponseMessage>? apiResponder = null)
    {
        apiResponder ??= _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"newValue\":1}", System.Text.Encoding.UTF8, "application/json")
        };

        var mock = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-jwt\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            return apiResponder(req);
        });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
            new HttpClient(mock, disposeHandler: false) { BaseAddress = new Uri("http://test-mycelium") });

        var myceliumLogger = new Mock<ILogger<MyceliumClient>>();
        var myceliumClient = new MyceliumClient(httpFactory.Object, myceliumLogger.Object, "http://test-mycelium", "consumes");

        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        return new Services.Metabolism(myceliumClient, engineLogger.Object, "consumes");
    }

    private static SimulationConfig MakePastConfig(
        string relId = "rel-1",
        decimal quantity = 5.0m,
        int freqSeconds = 60,
        DateTime? endUtc = null,
        decimal startDelaySeconds = 0m) =>
        new(relId, "subject-1", "target-1", "TestSubject", quantity, "kWh", "quantity", freqSeconds,
            DateTime.UtcNow.AddSeconds(-10), // past start
            endUtc ?? DateTime.UtcNow.AddHours(1),
            startDelaySeconds);

    [Fact]
    public async Task RunSimulationLoop_PastStartTime_ActivatesImmediately()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig());

        // Wait enough time for stagger delay + status change (stagger = order*200 + up to 500ms jitter)
        await Task.Delay(1500);

        entry.Status.Should().Be("active");
    }

    [Fact]
    public async Task RunSimulationLoop_ExecutesTick_IncrementsTickCount()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 1));

        // Wait for stagger + at least one tick cycle (stagger up to ~700ms, then 1s freq)
        await Task.Delay(2500);

        entry.TickCount.Should().BeGreaterThan(0);
        entry.LastTickUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunSimulationLoop_MyceliumError_SetsLastError()
    {
        var engine = CreateEngineWithMockedMycelium(req =>
        {
            // Token requests succeed, quantity requests fail
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-jwt\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Mycelium down", System.Text.Encoding.UTF8, "text/plain")
            };
        });

        var entry = engine.Register(MakePastConfig(freqSeconds: 1));

        // Wait for stagger + at least one tick attempt
        await Task.Delay(2500);

        entry.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunSimulationLoop_Cancel_SetsStatusCancelled()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 60));

        // Wait for the simulation to become active
        await Task.Delay(1500);
        entry.Status.Should().Be("active");

        // Now cancel it
        engine.Cancel("rel-1");

        // Wait for the cancellation to take effect
        await Task.Delay(500);

        entry.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task RunSimulationLoop_EndUtcReached_SetsCompleted()
    {
        var engine = CreateEngineWithMockedMycelium();
        // Set endUtc very close to now so the loop exits quickly after activation
        var entry = engine.Register(MakePastConfig(freqSeconds: 1, endUtc: DateTime.UtcNow.AddSeconds(2)));

        // Wait for stagger + enough time for endUtc to pass
        await Task.Delay(4000);

        entry.Status.Should().Be("completed");
    }

    [Fact]
    public async Task StopAllAsync_CancelsAndAwaitsAll()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry1 = engine.Register(MakePastConfig(relId: "rel-1", freqSeconds: 60));
        var entry2 = engine.Register(MakePastConfig(relId: "rel-2", freqSeconds: 60));

        // Wait for both to activate
        await Task.Delay(1500);

        await engine.StopAllAsync();

        entry1.Status.Should().Be("cancelled");
        entry2.Status.Should().Be("cancelled");
        engine.GetAll().Should().BeEmpty();
    }

    #endregion

    #region StartDelaySeconds behavior

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_SetsDelayedStatusFirst()
    {
        var engine = CreateEngineWithMockedMycelium();
        // 2s delay + past start time — should show "delayed" before activating
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 2.0m));

        // Immediately after registration, should be "delayed" (not "active")
        await Task.Delay(200);
        entry.Status.Should().Be("delayed");

        // No ticks should have fired during the delay
        entry.TickCount.Should().Be(0);
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_ActivatesAfterDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        // 1s delay + past start time — should activate after delay
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 1.0m));

        // Wait for delay (1s) + stagger (~700ms max) + margin
        await Task.Delay(2500);

        entry.Status.Should().Be("active");
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_NoTicksDuringDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        // 2s delay with short frequency — should NOT tick during the delay period
        var entry = engine.Register(MakePastConfig(freqSeconds: 1, startDelaySeconds: 2.0m));

        // Check at 500ms — still in delay phase
        await Task.Delay(500);
        entry.TickCount.Should().Be(0);
        entry.Status.Should().Be("delayed");
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_CancelDuringDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 10.0m));

        await Task.Delay(200);
        entry.Status.Should().Be("delayed");

        engine.Cancel("rel-1");
        await Task.Delay(200);

        entry.Status.Should().Be("cancelled");
        entry.TickCount.Should().Be(0);
    }

    [Fact]
    public async Task RunSimulationLoop_ZeroStartDelay_SkipsDelayPhase()
    {
        var engine = CreateEngineWithMockedMycelium();
        // 0 delay (default) — should go straight to stagger, then active
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 0m));

        // Wait for stagger
        await Task.Delay(1500);

        // Should never have been "delayed" — went straight to active
        entry.Status.Should().Be("active");
    }

    #endregion
}
