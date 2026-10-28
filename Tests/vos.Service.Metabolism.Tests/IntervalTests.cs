using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Services;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// The guide tells authors to write frequencySeconds as a vos.Decimal, and a decimal written 10.0 arrives
// with its decimal point.
public class IntervalTests : IAsyncLifetime
{
    private readonly MetabolismWebApplicationFactory _factory = new();
    private readonly List<Services.Metabolism> _engines = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        foreach (var engine in _engines)
            await engine.StopAllAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private Task<HttpResponseMessage> HandleWithInterval(string frequencySecondsAsWritten) =>
        _client.PostAsync("/handle", new StringContent($$"""
            {
              "relationshipId": "{{Guid.NewGuid()}}",
              "subjectId": "{{Guid.NewGuid()}}",
              "targetId": "{{Guid.NewGuid()}}",
              "properties": {
                "quantity": 1.5,
                "frequencySeconds": {{frequencySecondsAsWritten}},
                "startUtc": "{{DateTime.UtcNow.AddHours(1):o}}"
              }
            }
            """, Encoding.UTF8, "application/json"));

    private Services.Metabolism Engine()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var engine = new Services.Metabolism(
            new MyceliumClient(httpFactory.Object, NullLogger<MyceliumClient>.Instance, "http://localhost:0", ResourceDirection.Consumes),
            NullLogger<Services.Metabolism>.Instance, ResourceDirection.Consumes);
        _engines.Add(engine);
        return engine;
    }

    private static SimulationConfig WaitingConfig(decimal frequencySeconds) =>
        new("rel-1", "subject-1", "target-1", "TestSubject", 5.0m, "kWh", "quantity", frequencySeconds,
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));

    [Theory]
    [InlineData("10.0", 10.0)]
    [InlineData("2.5", 2.5)]
    [InlineData("1", 1.0)]
    public async Task Bug8007_AnIntervalWrittenWithADecimalPoint_StartsASimulationAtThatInterval(
        string asWritten, double expectedSeconds)
    {
        var response = await HandleWithInterval(asWritten);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var simulations = await _client.GetFromJsonAsync<JsonElement[]>("/simulations");
        simulations!.Single().GetProperty("frequencySeconds").GetDecimal().Should().Be((decimal)expectedSeconds);
    }

    // An interval of 0 pauses for nothing, so every tick's writes follow the last with no wait.
    [Theory]
    [InlineData("0.4")]
    [InlineData("0.999")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Bug8007_AnIntervalUnderOneSecond_IsRefusedAndStartsNothing(string asWritten)
    {
        var response = await HandleWithInterval(asWritten);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("frequencySeconds");
        (await _client.GetFromJsonAsync<JsonElement[]>("/simulations")).Should().BeEmpty();
    }

    [Fact]
    public void Bug8007_AChangedIntervalKeepsItsFraction()
    {
        var engine = Engine();
        engine.Register(WaitingConfig(60m));

        engine.UpdateProperty("rel-1", "frequencySeconds", JsonDocument.Parse("2.5").RootElement);

        engine.GetAll().Single().Config.FrequencySeconds.Should().Be(2.5m);
    }

    [Theory]
    [InlineData("0.4")]
    [InlineData("0.5")]
    [InlineData("0")]
    public void Bug8007_AChangeToAnIntervalUnderOneSecond_IsRefusedAndTheSimulationKeepsItsInterval(string asWritten)
    {
        var engine = Engine();
        engine.Register(WaitingConfig(60m));

        engine.UpdateProperty("rel-1", "frequencySeconds", JsonDocument.Parse(asWritten).RootElement);

        engine.GetAll().Single().Config.FrequencySeconds.Should().Be(60m);
    }

    [Fact]
    public void Bug8007_ThePauseBetweenTicksIsTheIntervalFractionIncluded()
    {
        WaitingConfig(2.5m).Interval.Should().Be(TimeSpan.FromMilliseconds(2500));
    }
}
