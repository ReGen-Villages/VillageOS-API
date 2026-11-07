using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public static class NodeChoices
{
    public static NodeRunResult Choose(DagNode node, IReadOnlyDictionary<string, JsonElement> inputs, NodeRunResult answer, TimeProvider clock)
    {
        if (answer.Status != RunStatus.Succeeded || node.DecisionPaths.Count == 0) return answer;

        var step = JsonSerializer.SerializeToElement(new { inputs, outputs = answer.Outputs });
        var holding = new List<DagDecisionPath>();
        foreach (var path in node.DecisionPaths.Where(path => path.When is not null))
        {
            JsonElement verdict;
            try
            {
                verdict = path.When!.Eval(step, clock, new Dictionary<string, JsonElement> { ["things"] = path.Things });
            }
            catch (Exception failure)
            {
                return Failed(answer, $"'{node.Name}' cannot judge its choice '{path.Name}': {failure.Message}");
            }

            if (verdict.ValueKind == JsonValueKind.True) holding.Add(path);
            else if (verdict.ValueKind is not (JsonValueKind.False or JsonValueKind.Null))
                return Failed(answer, $"'{node.Name}' cannot judge its choice '{path.Name}': its rule answered {verdict.GetRawText()}, and a rule answers true or false.");
        }

        if (holding.Count > 1)
            return Failed(answer, $"'{node.Name}' has more than one choice whose rule holds: {string.Join(", ", holding.Select(path => $"'{path.Name}'"))}.");

        var taken = holding.Count == 1 ? holding[0] : node.DecisionPaths.FirstOrDefault(path => path.When is null);
        return taken is null
            ? Failed(answer, $"'{node.Name}' succeeded, but no rule of its choices holds and it has no choice without a rule.")
            : answer with { Decision = taken.Id };
    }

    private static NodeRunResult Failed(NodeRunResult answer, string error) =>
        answer with { Status = RunStatus.Failed, Outputs = new Dictionary<string, JsonElement>(), Error = error };
}
