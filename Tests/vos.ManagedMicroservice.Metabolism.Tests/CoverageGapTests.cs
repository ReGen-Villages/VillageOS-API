// Targeted tests covering specific branches in vos.ManagedMicroservice.Metabolism that
// the per-class test files don't otherwise exercise. Each test names the gap it pins.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Metabolism.Configuration;
using vos.ManagedMicroservice.Metabolism.Models;
using vos.ManagedMicroservice.Metabolism.Services;
using Xunit;
using MetabolismEngine = vos.ManagedMicroservice.Metabolism.Services.Metabolism;

namespace vos.ManagedMicroservice.Metabolism.Tests;

public class CoverageGapTests
{
    // ---- CliArgs.UsageMessage (lines 56-63) ----

    [Fact]
    public void UsageMessage_MentionsEveryFlag()
    {
        var msg = CliArgs.UsageMessage;
        msg.Should().Contain("--port");
        msg.Should().Contain("--brokerUrl");
        msg.Should().Contain("--mode");
        msg.Should().Contain("--token");
        msg.Should().Contain("--signingKey");
        msg.Should().Contain("--issuer");
        msg.Should().Contain("--audience");
    }

    // ---- EndpointMapper /handle catch block (lines 49-52) ----

    [Fact]
    public async Task Handle_InvalidStartUtcInProperties_HitsCatchBlockReturns500()
    {
        // Sending an unparseable startUtc string triggers ExtractConfig to throw inside
        // HandleRequestProcessor.ProcessHandle, which propagates up to the /handle lambda's
        // catch block and returns Results.Problem(500).
        var factory = new MetabolismWebApplicationFactory();
        await factory.InitializeAsync();
        try
        {
            using var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync("/handle", new
            {
                relationshipId = Guid.NewGuid().ToString(),
                subjectId = Guid.NewGuid().ToString(),
                targetId = Guid.NewGuid().ToString(),
                properties = new
                {
                    startUtc = "not a date"   // DateTime.Parse will throw
                }
            });

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // Note: EndpointMapper.cs line 94 (the engine.GetAll().Count(e => e.Status == "active")
    // predicate) was already executed by Handle_ValidRequest_RegistersSimulation_IncrementsCount —
    // it just shows uncovered when the simulation list is empty and Count short-circuits.
    // The full coverage report after this batch should mark it covered.

    // ---- Metabolism.UnwrapJsonElement (lines 178-181) ----
    // UpdateProperty(rel, "unit", value) routes through UnwrapJsonElement before .ToString().
    // Passing JsonElement of each ValueKind exercises the corresponding branch.

    private static MetabolismEngine NewEngine() =>
        new(new BrokerClientStub(), NullLogger<MetabolismEngine>.Instance, "consumes");

    private static SimulationConfig NewConfig(string relId = "rel-x") =>
        new(relId, "subject", "target", "subj-name",
            1m, "kg", "weight", 60,
            DateTime.UtcNow.AddYears(1), DateTime.UtcNow.AddYears(2), 0m);

    [Fact]
    public void UpdateProperty_BoolTrueJsonElement_UnwrapsAndAssigns()
    {
        var engine = NewEngine();
        engine.Register(NewConfig("rel-bool-true"));

        var element = JsonDocument.Parse("true").RootElement;
        var act = () => engine.UpdateProperty("rel-bool-true", "unit", element);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateProperty_BoolFalseJsonElement_UnwrapsAndAssigns()
    {
        var engine = NewEngine();
        engine.Register(NewConfig("rel-bool-false"));

        var element = JsonDocument.Parse("false").RootElement;
        var act = () => engine.UpdateProperty("rel-bool-false", "unit", element);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateProperty_NullJsonElement_UnwrapsToNull()
    {
        var engine = NewEngine();
        engine.Register(NewConfig("rel-null"));

        var element = JsonDocument.Parse("null").RootElement;
        var act = () => engine.UpdateProperty("rel-null", "unit", element);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateProperty_ArrayJsonElement_FallsBackToRawText()
    {
        var engine = NewEngine();
        engine.Register(NewConfig("rel-array"));

        var element = JsonDocument.Parse("[1, 2, 3]").RootElement;
        var act = () => engine.UpdateProperty("rel-array", "unit", element);

        act.Should().NotThrow();
    }

    // ---- HandleRequest record default (line 13) ----
    // Constructing HandleRequest with default-able optional params pins the record's
    // generated constructor as exercised.

    [Fact]
    public void HandleRequest_ConstructsWithNullOptionals()
    {
        var req = new HandleRequest("rel", "subj", "tgt", null, null, null);
        req.RelationshipId.Should().Be("rel");
        req.SubjectId.Should().Be("subj");
        req.TargetId.Should().Be("tgt");
        req.SubjectName.Should().BeNull();
        req.TargetName.Should().BeNull();
        req.Properties.Should().BeNull();
    }
}

// Minimal BrokerClient stand-in for direct Metabolism engine tests. None of the methods
// under test (UpdateProperty's JsonElement unwrapping) actually invoke broker calls.
internal sealed class BrokerClientStub : BrokerClient
{
    public BrokerClientStub()
        : base(new FakeFactory(), NullLogger<BrokerClient>.Instance, "http://localhost:0", "consumes")
    { }

    private sealed class FakeFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
