using System.Text.Json;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Services;
using vos.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace vos.Service.Metabolism.Tests;

public class MetabolismTests : IAsyncLifetime
{
    private readonly Services.Metabolism _engine;

    // A registered simulation keeps ticking for the rest of the test session unless it is stopped,
    // so every engine a test builds is stopped when that test ends. Left running they compete with
    // the tests that come after them for the same machine.
    private readonly List<Services.Metabolism> _startedEngines = new();

    public MetabolismTests()
    {
        // MyceliumClient needs IHttpClientFactory — mock it so simulation ticks fail harmlessly
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var myceliumLogger = new Mock<ILogger<MyceliumClient>>();
        var myceliumClient = new MyceliumClient(httpFactory.Object, myceliumLogger.Object, "http://localhost:0", ResourceDirection.Consumes);

        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        _engine = new Services.Metabolism(myceliumClient, engineLogger.Object, ResourceDirection.Consumes);
        _startedEngines.Add(_engine);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var engine in _startedEngines)
            await engine.StopAllAsync();
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

    // A string-valued property reaches Convert.ToDecimal as a string, which reads the machine's
    // regional format. The value comes off the model, so the dot is always a decimal point.
    [Fact]
    public void UpdateProperty_StringQuantity_ReadsTheDotAsADecimalPointWhateverTheRegionalFormat()
    {
        _engine.Register(MakeConfig());

        var quantity = TestCulture.In(TestCulture.CommaDecimal, () =>
        {
            _engine.UpdateProperty("rel-1", "quantity", "10.5");
            return _engine.GetAll().First().Config.Quantity;
        });

        quantity.Should().Be(10.5m);
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

        _engine.UpdateProperty("rel-1", "total_consumed", 999.0m);

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
    private Services.Metabolism CreateEngineWithMockedMycelium(
        Func<HttpRequestMessage, HttpResponseMessage>? apiResponder = null,
        ResourceDirection? direction = null)
    {
        direction ??= ResourceDirection.Consumes;

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
        var myceliumClient = new MyceliumClient(httpFactory.Object, myceliumLogger.Object, "http://test-mycelium", direction);

        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        var engine = new Services.Metabolism(myceliumClient, engineLogger.Object, direction);
        _startedEngines.Add(engine);
        return engine;
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

        await Settle.UntilAsync(() => entry.Status == "active",
            "a simulation whose start time has already passed activates once its stagger delay ends");
    }

    // Regression (#6512): the running total a tick writes was chosen by comparing the launch word
    // against a literal, so a misspelling recorded consumption as production and nothing failed.
    [Theory]
    [InlineData("consumes", "total_consumed", "decrements")]
    [InlineData("produces", "total_produced", "increments")]
    public async Task RunSimulationLoop_WritesTheRunningTotalAndPoolCallItsDirectionNames(
        string launchArgument, string expectedTrackingProperty, string expectedPoolAction)
    {
        var paths = new System.Collections.Concurrent.ConcurrentBag<string>();
        var engine = CreateEngineWithMockedMycelium(
            req =>
            {
                paths.Add(req.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"newValue\":1}", System.Text.Encoding.UTF8, "application/json")
                };
            },
            ResourceDirection.Parse(launchArgument));

        engine.Register(MakePastConfig(freqSeconds: 1));

        // The running total is written last in a tick, so once it arrives the pool call already has.
        var trackingPath = $"/api/relationships/rel-1/properties/{expectedTrackingProperty}/increments";
        await Settle.UntilAsync(() => paths.Contains(trackingPath),
            $"a tick writes the running total to {trackingPath}");

        paths.Should().Contain($"/api/things/target-1/properties/quantity/{expectedPoolAction}");
    }

    [Fact]
    public async Task RunSimulationLoop_ExecutesTick_IncrementsTickCount()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 1));

        await Settle.UntilAsync(() => entry.TickCount > 0, "the loop runs its first tick");

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

        await Settle.UntilAsync(() => !string.IsNullOrEmpty(entry.LastError),
            "a tick whose pool call is refused records why it failed");
    }

    [Fact]
    public async Task RunSimulationLoop_Cancel_SetsStatusCancelled()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 60));
        await Settle.UntilAsync(() => entry.Status == "active", "the simulation is running before it is cancelled");

        engine.Cancel("rel-1");

        await Settle.UntilAsync(() => entry.Status == "cancelled", "cancelling a running simulation ends its loop");
    }

    [Fact]
    public async Task RunSimulationLoop_EndUtcReached_SetsCompleted()
    {
        var engine = CreateEngineWithMockedMycelium();
        // An end time close to now, so the loop reaches it after a tick or two rather than in an hour.
        var entry = engine.Register(MakePastConfig(freqSeconds: 1, endUtc: DateTime.UtcNow.AddSeconds(2)));

        await Settle.UntilAsync(() => entry.Status == "completed", "the loop stops once its end time has passed");
    }

    [Fact]
    public async Task StopAllAsync_CancelsAndAwaitsAll()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry1 = engine.Register(MakePastConfig(relId: "rel-1", freqSeconds: 60));
        var entry2 = engine.Register(MakePastConfig(relId: "rel-2", freqSeconds: 60));
        await Settle.UntilAsync(() => entry1.Status == "active" && entry2.Status == "active",
            "both simulations are running before they are stopped");

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
        // A delay long enough to outlast the test, so nothing here depends on when it expires.
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 10.0m));

        await Settle.UntilAsync(() => entry.Status == "delayed",
            "a start delay holds the simulation in the delayed phase instead of activating it");
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_ActivatesAfterDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 1.0m));

        await Settle.UntilAsync(() => entry.Status == "active",
            "the simulation activates once its start delay has run out");
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_NoTicksDuringDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        // A one-second frequency, so a loop that ticked during its delay would be caught, and a delay
        // long enough that reading the tick count cannot race its expiry.
        var entry = engine.Register(MakePastConfig(freqSeconds: 1, startDelaySeconds: 10.0m));

        await Settle.UntilAsync(() => entry.Status == "delayed", "the simulation enters its start delay");

        entry.TickCount.Should().Be(0);
    }

    [Fact]
    public async Task RunSimulationLoop_WithStartDelay_CancelDuringDelay()
    {
        var engine = CreateEngineWithMockedMycelium();
        var entry = engine.Register(MakePastConfig(freqSeconds: 60, startDelaySeconds: 10.0m));
        await Settle.UntilAsync(() => entry.Status == "delayed", "the simulation is inside its start delay");

        engine.Cancel("rel-1");

        await Settle.UntilAsync(() => entry.Status == "cancelled",
            "cancelling during the start delay ends the loop before it activates");
        entry.TickCount.Should().Be(0);
    }

    #endregion
}
