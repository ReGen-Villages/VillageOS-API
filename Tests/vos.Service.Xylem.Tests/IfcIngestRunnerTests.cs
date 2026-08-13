using vos.Service.Xylem.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class IfcIngestRunnerTests
{
    [Fact]
    public async Task RunAsync_missing_tool_reports_a_clear_error_without_spawning()
    {
        var runner = new IfcIngestRunner("/no/such/IfcIngest.dll", "http://localhost:5000", "tok",
            NullLogger<IfcIngestRunner>.Instance);

        var r = await runner.RunAsync("/tmp/whatever.ifc", "Demo", default);

        r.Success.Should().BeFalse();
        r.Error.Should().Contain("not found");
    }

    [Fact]
    public void The_ingest_process_is_handed_its_token_through_the_environment()
    {
        var startInfo = new IfcIngestRunner("/tools/IfcIngest.dll", "http://localhost:5000", "the.service.jwt",
            NullLogger<IfcIngestRunner>.Instance).BuildStartInfo("/tmp/model.ifc", "Demo");

        startInfo.Environment["Token"].Should().Be("the.service.jwt");
        startInfo.ArgumentList.Should().NotContain("--token").And.NotContain("the.service.jwt");
    }

    [Fact]
    public void An_absent_token_leaves_the_ingest_process_without_one()
    {
        var startInfo = new IfcIngestRunner("/tools/IfcIngest.dll", "http://localhost:5000", null,
            NullLogger<IfcIngestRunner>.Instance).BuildStartInfo("/tmp/model.ifc", "Demo");

        startInfo.Environment.Should().NotContainKey("Token");
    }

    [Theory]
    [InlineData("Ingested 12 things, 5 relationships.\nFragment POST: 200 applied", 12, 5)]
    [InlineData("Ingested 0 things, 0 relationships.", 0, 0)]
    [InlineData("no count line here", 0, 0)]
    public void ParseCounts_reads_the_totals(string stdout, int things, int rels)
    {
        IfcIngestRunner.ParseCounts(stdout).Should().Be((things, rels));
    }
}
