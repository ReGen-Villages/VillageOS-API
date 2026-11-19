using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using vos.Hypha.Registry;
using vos.MyceliumClient;
using Xunit;

namespace vos.Hypha.Tests;

public class EveryToolDeclaresWhatItNeedsAndWhatItDoesTests
{
    [Fact]
    public void The_servers_own_tools_break_no_rule()
    {
        using var services = new ServiceCollection()
            .AddPlatformServer(new MyceliumClient(ServerUnderTest.MyceliumUrl, ServerUnderTest.ApiKey))
            .Services.BuildServiceProvider();
        var tools = services.GetServices<McpServerTool>().ToList();

        tools.Should().NotBeEmpty("the rules must be asked of the tools the server ships, or they pass over nothing");
        ToolRules.Broken(tools).Should().BeEmpty();
    }

    [Fact]
    public void A_tool_that_declares_no_policy_breaks_a_rule() =>
        BrokenBy(nameof(Offenders.NoPolicy)).Should().ContainSingle().Which.Should().Contain("declares no policy");

    [Fact]
    public void A_tool_marked_as_only_reading_whose_policy_writes_breaks_a_rule() =>
        BrokenBy(nameof(Offenders.ReadsButNeedsAWritingPolicy)).Should().ContainSingle()
            .Which.Should().Contain("only reads").And.Contain(nameof(MyceliumPolicy.ModifyData));

    [Theory]
    [InlineData(nameof(Offenders.DeleteThing))]
    [InlineData(nameof(Offenders.PruneBelow))]
    [InlineData(nameof(Offenders.PromoteGroup))]
    [InlineData(nameof(Offenders.DecideAbout))]
    public void A_tool_that_deletes_prunes_promotes_or_decides_and_is_not_marked_as_destroying_breaks_a_rule(string tool) =>
        BrokenBy(tool).Should().ContainSingle().Which.Should().Contain("not marked as destroying");

    [Fact]
    public void A_tool_that_keeps_every_rule_breaks_none() =>
        BrokenBy(nameof(Offenders.DeleteThingMarked)).Should().BeEmpty();

    private static IEnumerable<string> BrokenBy(string method) =>
        ToolRules.Broken([McpServerTool.Create(typeof(Offenders).GetMethod(method)!, target: null)]);

    private static class Offenders
    {
        [McpServerTool(Name = "read_something", ReadOnly = true)]
        public static string NoPolicy() => "";

        [McpServerTool(Name = "read_something", ReadOnly = true)]
        [RequiresPolicy(MyceliumPolicy.ModifyData)]
        public static string ReadsButNeedsAWritingPolicy() => "";

        [McpServerTool(Name = "delete_thing")]
        [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
        public static string DeleteThing() => "";

        [McpServerTool(Name = "prune")]
        [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
        public static string PruneBelow() => "";

        [McpServerTool(Name = "promote_part", Destructive = false)]
        [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
        public static string PromoteGroup() => "";

        [McpServerTool(Name = "decide")]
        [RequiresPolicy(MyceliumPolicy.ModifyData)]
        public static string DecideAbout() => "";

        [McpServerTool(Name = "delete_thing", Destructive = true)]
        [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
        public static string DeleteThingMarked() => "";
    }
}
