using vos.Service.Stomata.Services;

namespace vos.Service.Stomata.Tests;

public static class DesignStrategy
{
    public const string ComfortableAsItIs = "comfortable-as-it-is";
    public const string WithAirMovement = "with-air-movement";
    public const string DehumidificationNeeded = "dehumidification-needed";
    public const string HeatingNeeded = "heating-needed";
    public const string DirectEvaporativeCooling = "direct-evaporative-cooling";
    public const string TwoStageEvaporativeCooling = "two-stage-evaporative-cooling";
    public const string CoolingSystemNeeded = "cooling-system-needed";
}

// A copy of the shared site template's design-strategy groups, which live in the platform's repository and not this one.
public static class TemplateGroups
{
    public static readonly IReadOnlyList<(string Name, long Order, IReadOnlyDictionary<string, bool> Tests)> Held =
    [
        (DesignStrategy.ComfortableAsItIs, 1, Tests((HourTests.InsideStillAirZone, true), (HourTests.HumidityRatioWithinLimit, true))),
        (DesignStrategy.WithAirMovement, 2, Tests((HourTests.InsideMovingAirZone, true), (HourTests.HumidityRatioWithinLimit, true))),
        (DesignStrategy.DehumidificationNeeded, 3, Tests((HourTests.InsideAComfortZone, true), (HourTests.HumidityRatioWithinLimit, false))),
        (DesignStrategy.HeatingNeeded, 4, Tests((HourTests.ColderThanWinterZone, true))),
        (DesignStrategy.DirectEvaporativeCooling, 5, Tests(
            (HourTests.WarmerThanSummerZone, true), (HourTests.HumidityRatioWithinLimit, true), (HourTests.ReachedByDirectEvaporation, true))),
        (DesignStrategy.TwoStageEvaporativeCooling, 6, Tests((HourTests.WarmerThanSummerZone, true), (HourTests.ReachedByTwoStageEvaporation, true))),
        (DesignStrategy.CoolingSystemNeeded, 7, Tests()),
    ];

    public static DesignStrategyGroups Groups(params DesignStrategyGroup[] tried) =>
        new(tried.Length > 0 ? tried : [.. Held.Select(group => new DesignStrategyGroup(Guid.NewGuid(), group.Name, group.Tests))], [], []);

    public static IReadOnlyDictionary<string, bool> Tests(params (string Test, bool Holds)[] tests) =>
        tests.ToDictionary(test => test.Test, test => test.Holds);
}
