using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

public class SeedVocabularyTests
{
    private static readonly HashSet<string> Declared = [.. SeedVocabulary.NamesTheModelDeclaresForItself(TestSeeds.Catchments)];

    [Fact]
    public void The_kinds_are_the_ones_no_role_binds_and_whose_members_no_role_binds() =>
        Assert.Superset(Set("Site", "Catchment", "Reservoir", "Spring", "SpringDecision"), Declared);

    [Fact]
    public void The_predicates_are_the_ones_no_role_binds_and_the_platform_does_not_find_by_name() =>
        Assert.Superset(Set("feeds", "decidedAs"), Declared);

    [Fact]
    public void The_properties_are_the_stored_and_derived_ones_of_those_kinds() =>
        Assert.Superset(
            Set("elevationMetres", "areaHectares", "runoffCoefficient", "quantityM3", "flowM3PerDay", "decidedAt",
                "storedM3", "daysOfSupply", "balanceM3PerYear"),
            Declared);

    [Fact]
    public void The_states_are_the_ranges_of_those_kinds_and_the_range_Things() =>
        Assert.Superset(
            Set("WaterResilient", "WaterShortOfTarget", "WaterNotAssessed", "SpringAwaitingDecision", "SpringDecided",
                "CatchmentAwaitingSurvey"),
            Declared);

    [Theory]
    [InlineData("Dashboard", "a kind a role binds")]
    [InlineData("Configuration", "a root every kind sits under")]
    [InlineData("Role", "the kind of the role Things")]
    [InlineData("Page", "a role Thing")]
    [InlineData("ConnectionTrigger", "a kind whose members a role binds")]
    [InlineData("Echo prototype", "a kind under a kind a role binds")]
    [InlineData("triggeredBy", "a predicate a role binds")]
    [InlineData("is", "a predicate the platform finds by name")]
    [InlineData("namesPropertyOf", "a predicate the platform finds by name")]
    [InlineData("wording", "a property of a member, which is data")]
    [InlineData("specification", "a property of a Thing under a kind a role binds")]
    [InlineData("Criteria", "a property of a range Thing")]
    [InlineData("tapped", "a member, whose name is data")]
    [InlineData("Ålvik", "a member, whose name is data")]
    public void What_the_platform_declares_and_what_is_data_are_left_out(string name, string because) =>
        Assert.False(Declared.Contains(name), $"{name} is {because}");

    [Fact]
    public void Nothing_else_is_declared()
    {
        string[] everyName =
        [
            "Site", "Catchment", "Reservoir", "Spring", "SpringDecision", "feeds", "decidedAs",
            "elevationMetres", "rainfallMillimetres", "utcOffsetSeconds",
            "areaHectares", "rainfallMillimetresPerYear", "siltDepthMetres", "surveyedAt", "runoffCoefficient",
            "lossFraction", "dailyDemandM3", "evaporationM3PerHectarePerYear", "irrigationM3PerHectarePerYear",
            "storedM3", "capacityM3", "springFlowM3PerDay", "harvestM3PerYear", "irrigationM3PerYear",
            "supplyM3PerYear", "balanceM3PerYear", "surfaceBalanceM3PerYear", "daysOfSupply", "fillFraction",
            "quantityM3", "flowM3PerDay", "decidedAt",
            "WaterResilient", "WaterShortOfTarget", "WaterNotAssessed", "SpringAwaitingDecision", "SpringDecided",
            "CatchmentAwaitingSurvey",
        ];

        Assert.Equal(everyName.Order(StringComparer.Ordinal), Declared.Order(StringComparer.Ordinal));
    }

    private static HashSet<string> Set(params string[] names) => [.. names];
}
