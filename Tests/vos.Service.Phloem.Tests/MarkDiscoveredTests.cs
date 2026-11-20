using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The discovery drawing stamps a site only once its words are written, because markDiscovered's one wire carries
// only on Resolve words' success.
public class MarkDiscoveredTests
{
    private static readonly RunSubject Site = new(Guid.NewGuid(), "Willow Bend");

    private static (TestGraphs.MarkDiscoveredFixture Drawing, FakeGateway Gateway) Run(bool resolveWordsSucceeds, bool reportsAFailure = false)
    {
        var drawing = TestGraphs.MarkDiscoveredPipeline(reportsAFailure);
        var gateway = new FakeGateway(drawing.Fixture.Build())
        {
            OnCall = (subdomain, _) => subdomain switch
            {
                "resolve" when resolveWordsSucceeds => new NodeCallResult(200, """{"result":"success","outputs":{"written":[]}}"""),
                "resolve" => new NodeCallResult(200, """{"result":"fail","error":"Could not write the relationships."}"""),
                _ => new NodeCallResult(200, """{"result":"success","outputs":{"site":{},"study":{}}}"""),
            },
        };
        return (drawing, gateway);
    }

    [Fact]
    public async Task A_run_whose_words_fail_and_whose_failure_is_reported_records_mark_discovered_not_taken_and_never_calls_it()
    {
        var (drawing, gateway) = Run(resolveWordsSucceeds: false, reportsAFailure: true);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(drawing.PipelineId, default, CancellationToken.None, subject: Site);

        gateway.Called.Should().Equal("resolve", "report");
        gateway.Records[(result.RunId, drawing.MarkDiscovered.Id)].Status.Should().Be(RunStatus.NotTaken);
    }

    [Fact]
    public async Task A_run_whose_words_fail_with_no_fail_wire_fails_without_calling_mark_discovered()
    {
        var (drawing, gateway) = Run(resolveWordsSucceeds: false);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(drawing.PipelineId, default, CancellationToken.None, subject: Site);

        result.Success.Should().BeFalse();
        gateway.Called.Should().Equal("resolve");
        gateway.Records[(result.RunId, drawing.MarkDiscovered.Id)].Status.Should().Be(RunStatus.Skipped);
    }

    [Fact]
    public async Task A_run_whose_words_are_written_calls_mark_discovered_with_the_site_from_its_subject()
    {
        var (drawing, gateway) = Run(resolveWordsSucceeds: true);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await executor.RunAsync(drawing.PipelineId, default, CancellationToken.None, subject: Site);

        gateway.Called.Should().Equal("resolve", "mark");
        var site = gateway.Envelopes.Single(sent => sent.Subdomain == "mark").Envelope.GetProperty("inputs").GetProperty("site");
        site.GetProperty("id").GetString().Should().Be(Site.Id.ToString());
    }
}
