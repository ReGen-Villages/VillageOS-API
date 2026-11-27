using System.Text.Json;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using static vos.BrokerContract.Tests.AgainstTheEngine.EngineArrangement;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The broker makes a range Thing into a range that keeps its state history only when a seed loads.
internal static class RunRetirementSeed
{
    private static readonly Role StateRecordRole = new("stateRecord", RoleIdentity.Of("stateRecord"));
    private static readonly Role RetiringStateRole = new("retiringState", RoleIdentity.Of("retiringState"));
    private static readonly Role HoldingRole = new("holding", RoleIdentity.Of("holding"));

    public static readonly TimeSpan Period = TimeSpan.FromHours(1);

    public static string Write()
    {
        var things = new List<object>();
        var relationships = new List<object>();

        Guid Thing(string name, bool isArchetype = false, Dictionary<string, object>? properties = null)
        {
            var id = Guid.NewGuid();
            things.Add(new { Id = id, Name = name, IsArchetype = isArchetype, Properties = properties ?? new Dictionary<string, object>() });
            return id;
        }

        void Relate(Guid subject, Guid predicate, Guid target, Dictionary<string, object>? properties = null) =>
            relationships.Add(new { Id = Guid.NewGuid(), Subject = subject, Predicate = predicate, Target = target, Properties = properties ?? new Dictionary<string, object>() });

        var binds = Thing(ModelNames.Binds);

        void Bind(Role role, Guid bound)
        {
            things.Add(new { Id = role.Id, Name = $"{role.Word} role", IsArchetype = false, Properties = new Dictionary<string, object>() });
            Relate(role.Id, binds, bound);
        }

        var @is = Thing(PlatformPredicates.Is);
        Thing(ModelNames.Of);
        var has = Thing(ModelNames.Has);
        Bind(HoldingRole, has);

        var runKind = Thing("PipelineRun", isArchetype: true);
        Bind(PipelineRoles.PipelineRun, runKind);
        Bind(PipelineRoles.NodeRun, Thing("NodeRun", isArchetype: true));
        Bind(PipelineRoles.Attempt, Thing("Attempt", isArchetype: true));
        Bind(PipelineRoles.Orchestrator, Thing("Phloem prototype", isArchetype: true));

        Bind(StateRecordRole, Thing("heldState"));
        var retiredFrom = Thing("retiredFrom");
        Bind(RetiringStateRole, retiredFrom);
        var range = Thing("Range", isArchetype: true);
        Bind(RangeRole, range);
        var judges = Thing("judges");
        Bind(JudgedThingRole, judges);

        var endedWell = Thing("RunEndedWell", properties: new()
        {
            ["Criteria"] = Typed("vos.String", $"status IN ('{RunStatus.Succeeded}', '{RunStatus.Cancelled}')"),
            ["KeepsStateHistory"] = Typed("vos.Boolean", true),
        });
        Relate(endedWell, @is, range);
        Relate(endedWell, judges, runKind);
        Relate(runKind, retiredFrom, endedWell, new() { ["afterSeconds"] = Typed("vos.LongInteger", (long)Period.TotalSeconds) });

        var file = Path.Combine(Path.GetTempPath(), $"run-retirement-{Guid.NewGuid():N}.seed.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { Id = Guid.NewGuid(), Name = "Run retirement", Things = things, Relationships = relationships }));
        return file;
    }

    private static Dictionary<string, object> Typed(string type, object value) => new() { ["typeInfo"] = type, ["value"] = value };
}
