using System.Text.Json;
using System.Text.Json.Nodes;

namespace vos.Pages.Tests;

// A small model in the shape a selection of Mycelium's answers with: a few reservoirs, the page that
// compares them, and a second page whose description cannot be read.
internal static class HandBuiltModel
{
    public const string PageRoleId = "a18768bc-b4e7-513c-864f-842d674c1858";
    public const string DisplaySettingsRoleId = "1cb6206a-2726-5008-8188-698b299f5665";
    public const string PredicateRoleId = "86743302-6588-5a93-b6a5-0dde78b092c1";

    public const string ReservoirLevelsPageId = "00000000-0000-4000-8000-000000000101";
    public const string UnreadablePageId = "00000000-0000-4000-8000-000000000102";
    public const string AsenReservoirId = "00000000-0000-4000-8000-000000000201";
    public const string OstraReservoirId = "00000000-0000-4000-8000-000000000202";
    public const string SpringReservoirId = "00000000-0000-4000-8000-000000000203";

    public const string GateWidget = "Gates to the reservoirs";

    public const string ReservoirsToDrainPageId = "00000000-0000-4000-8000-000000000103";
    public const string DrainWidget = "Reservoirs to drain";

    public const string RunningLow = "RunningLow";
    public const string Capacity = "capacityCubicMetres";

    private const string IsId = "00000000-0000-4000-8000-000000000001";
    private const string BindsId = "00000000-0000-4000-8000-000000000002";
    private const string RoleKindId = "00000000-0000-4000-8000-000000000011";
    private const string PredicateKindId = "00000000-0000-4000-8000-000000000012";
    private const string PageKindId = "00000000-0000-4000-8000-000000000013";
    private const string ReservoirKindId = "00000000-0000-4000-8000-000000000014";
    private const string SettingsId = "00000000-0000-4000-8000-000000000021";

    private static readonly object s_reservoirLevels = new
    {
        title = "Reservoir levels",
        compare = new { label = "Reservoir", archetype = "Reservoir" },
        sections = new object[]
        {
            new
            {
                title = "Levels",
                widgets = new object[]
                {
                    new { type = "kpi", title = "Capacity", value = new { kind = "property", thing = "$scope", property = Capacity } },
                    new { type = "kpi", title = "Reservoirs running low", value = new { kind = "stateCount", state = RunningLow } },
                    new { type = "table", title = "Reservoirs", rows = new { kind = "thingList", archetype = "Reservoir" } },
                    new
                    {
                        type = "action",
                        title = "Reservoirs to inspect",
                        rows = new { kind = "thingList", archetype = "Reservoir" },
                        asks = new object[] { new { key = "depthMetres", label = "Depth", kind = "number" } },
                        writes = new { via = "inspections", archetype = "Inspection", predicate = "about", choices = new object[] { new { label = "Cleared" } } },
                    },
                    new
                    {
                        type = "action",
                        title = GateWidget,
                        rows = new { kind = "thingList", archetype = "Reservoir" },
                        writes = new
                        {
                            via = "gates",
                            choices = new object[] { new { label = "Set the code", act = "setCode", asks = new object[] { new { key = "gateCode", label = "Code", kind = "secret" } } } },
                        },
                    },
                    new
                    {
                        type = "lineSeries",
                        title = "Inflow asked the wrong way round",
                        series = new object[]
                        {
                            new
                            {
                                label = "Inflow",
                                value = new
                                {
                                    kind = "timeseries",
                                    archetype = "Reservoir",
                                    happenedAt = "measuredAt",
                                    reduction = "count",
                                    bucketSeconds = 3600,
                                    buckets = 24,
                                    scope = new { viaPredicate = "feeds", direction = "in" },
                                },
                            },
                        },
                    },
                },
            },
        },
    };

    private static readonly object s_reservoirsToDrain = new
    {
        title = DrainWidget,
        sections = new object[]
        {
            new
            {
                title = "Draining",
                widgets = new object[]
                {
                    new
                    {
                        type = "action",
                        title = DrainWidget,
                        rows = new { kind = "thingList", archetype = "Reservoir" },
                        asks = new object[]
                        {
                            new { key = "into", label = "Into", kind = "choice", options = new { kind = "stateList", state = RunningLow, archetype = "Reservoir" } },
                        },
                        writes = new
                        {
                            via = "draining",
                            choices = new object[] { new { label = "Drain now" }, new { label = "Drain tomorrow" }, new { label = "Leave full" } },
                        },
                    },
                },
            },
        },
    };

    public static SelectedThings Selected()
    {
        var things = new JsonArray
        {
            Thing(PageRoleId, "Page"),
            Thing(DisplaySettingsRoleId, "DisplaySettings"),
            Thing(PredicateRoleId, "Predicate"),
            Thing(IsId, "is"),
            Thing(BindsId, "binds"),
            Thing(RoleKindId, "Role", isKind: true),
            Thing(PredicateKindId, "Predicate", isKind: true),
            Thing(PageKindId, "Page", isKind: true),
            WithFigures(Thing(ReservoirKindId, "Reservoir", isKind: true, ("lossFraction", "vos.Double", 0.1))),
            Thing(SettingsId, "Display settings"),
            Thing(ReservoirLevelsPageId, "Reservoir levels", ("specification", "vos.String", JsonSerializer.Serialize(s_reservoirLevels))),
            Thing(UnreadablePageId, "Catchment inflow", ("specification", "vos.String", "{ this is not a page description")),
            Thing(ReservoirsToDrainPageId, DrainWidget, ("specification", "vos.String", JsonSerializer.Serialize(s_reservoirsToDrain))),
            Thing(AsenReservoirId, "Åsen reservoir", (Capacity, "vos.Double", 100.0)),
            Thing(OstraReservoirId, "Östra reservoir", (Capacity, "vos.Double", 300.0)),
            Thing(SpringReservoirId, "Spring reservoir", (Capacity, "vos.Double", 200.0)),
        };
        var relationships = new JsonArray();
        void Relate(string subject, string predicate, string target) => relationships.Add(new JsonObject
        {
            ["Id"] = $"00000000-0000-4000-9000-{relationships.Count:D12}",
            ["Name"] = null,
            ["SubjectId"] = subject,
            ["PredicateId"] = predicate,
            ["TargetId"] = target,
            ["Properties"] = new JsonObject(),
            ["InheritedOverrides"] = null,
            ["States"] = new JsonArray(),
        });

        foreach (var role in new[] { PageRoleId, DisplaySettingsRoleId, PredicateRoleId }) Relate(role, IsId, RoleKindId);
        Relate(PageRoleId, BindsId, PageKindId);
        Relate(DisplaySettingsRoleId, BindsId, SettingsId);
        Relate(PredicateRoleId, BindsId, PredicateKindId);
        Relate(IsId, IsId, PredicateKindId);
        Relate(BindsId, IsId, PredicateKindId);
        Relate(ReservoirLevelsPageId, IsId, PageKindId);
        Relate(UnreadablePageId, IsId, PageKindId);
        Relate(ReservoirsToDrainPageId, IsId, PageKindId);
        foreach (var reservoir in new[] { AsenReservoirId, OstraReservoirId, SpringReservoirId }) Relate(reservoir, IsId, ReservoirKindId);

        return new SelectedThings(things.ToJsonString(), relationships.ToJsonString());
    }

    public const string UsableCapacity = "usableCubicMetres";
    public const string Inflow = "inflowLitresPerDay";

    /// <summary>A figure worked out from the Thing's own values, and one totalled over the springs that feed it.</summary>
    private static JsonObject WithFigures(JsonObject kind)
    {
        kind["DerivedProperties"] = new JsonObject
        {
            [UsableCapacity] = new JsonObject
            {
                ["Expression"] = $"{Capacity} * (1 - lossFraction)",
                ["Reads"] = new JsonArray(Capacity, "lossFraction"),
                ["RisesWith"] = new JsonArray(Capacity),
                ["FallsWith"] = new JsonArray("lossFraction"),
            },
            [Inflow] = new JsonObject
            {
                ["Function"] = "Sum",
                ["RelatedType"] = "Spring",
                ["Reads"] = new JsonArray("flowLitresPerDay"),
                ["RisesWith"] = new JsonArray("flowLitresPerDay"),
            },
        };
        return kind;
    }

    private static JsonObject Thing(string id, string name, params (string Property, string Type, object Value)[] properties) =>
        Thing(id, name, isKind: false, properties);

    private static JsonObject Thing(string id, string name, bool isKind, params (string Property, string Type, object Value)[] properties)
    {
        var held = new JsonObject();
        foreach (var (property, type, value) in properties)
            held[property] = new JsonObject { ["typeInfo"] = type, ["value"] = JsonValue.Create(value) };
        return new JsonObject
        {
            ["Id"] = id,
            ["Name"] = name,
            ["IsArchetype"] = isKind,
            ["Properties"] = held,
            ["DerivedProperties"] = null,
            ["InheritedOverrides"] = null,
            ["States"] = new JsonArray(),
            ["Relationships"] = new JsonArray(),
        };
    }
}
