using System.ComponentModel;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace vos.Hypha.Tests;

public class NoToolNamesAWordOfOneModelTests
{
    private static readonly string[] NamesOneModelDeclares = ["Catchment", "areaHectares", "WaterShortOfTarget"];

    [Theory]
    [InlineData(nameof(Wordings.NamedForAKind), "Catchment")]
    [InlineData(nameof(Wordings.DescribedWithAState), "WaterShortOfTarget")]
    [InlineData(nameof(Wordings.TakingAKindsIdentifier), "Catchment")]
    [InlineData(nameof(Wordings.DescribingAParameterWithAProperty), "areaHectares")]
    public void A_name_of_one_model_is_found_wherever_a_tool_words_it(string tool, string name) =>
        Found(tool).Should().ContainSingle().Which.Should().Contain($"'{name}'");

    [Fact]
    public void A_tool_worded_in_the_platforms_own_terms_holds_none() =>
        Found(nameof(Wordings.InThePlatformsTerms)).Should().BeEmpty();

    private static IEnumerable<string> Found(string method) =>
        ToolWording.NamesOfOneModelIn(
            [McpServerTool.Create(typeof(Wordings).GetMethod(method)!, target: null)], NamesOneModelDeclares);

    private static class Wordings
    {
        [McpServerTool(Name = "list_catchments")]
        [Description("Lists Things.")]
        public static string NamedForAKind() => "";

        [McpServerTool(Name = "list_short")]
        [Description("Lists the Things in the state WaterShortOfTarget.")]
        public static string DescribedWithAState() => "";

        [McpServerTool(Name = "read_one")]
        [Description("Reads a Thing.")]
        public static string TakingAKindsIdentifier(Guid catchmentId) => catchmentId.ToString();

        [McpServerTool(Name = "set_one")]
        [Description("Sets a property.")]
        public static string DescribingAParameterWithAProperty([Description("For example areaHectares.")] string property) => property;

        [McpServerTool(Name = "get_thing")]
        [Description("Reads one Thing by its identifier or its name.")]
        public static string InThePlatformsTerms(string thing) => thing;
    }
}
