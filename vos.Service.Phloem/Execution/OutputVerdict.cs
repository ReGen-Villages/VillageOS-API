using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Execution;

public sealed record DeliveredFailure(string Node, string Port, string? Error);

public sealed record OutputVerdict(bool Passes, string? Reason)
{
    private static readonly OutputVerdict Passed = new(true, null);

    public static OutputVerdict Judge(
        DagNode output, Func<IReadOnlyDictionary<string, JsonElement>> inputs, IReadOnlyList<DeliveredFailure> failures, TimeProvider clock)
    {
        if (output.PassesWhen is null)
            return failures.Count == 0 ? Passed : new(false, $"A fail path reached '{output.Name}'.");

        JsonElement verdict;
        try
        {
            var reached = JsonSerializer.SerializeToElement(new
            {
                inputs = inputs(),
                failures = failures.Select(failure => new { node = failure.Node, port = failure.Port, error = failure.Error }),
            });
            verdict = output.PassesWhen.Eval(reached, clock);
        }
        catch (Exception failure)
        {
            return new(false, $"'{output.Name}' cannot judge the run: {failure.Message}");
        }

        return verdict.ValueKind switch
        {
            JsonValueKind.True => Passed,
            JsonValueKind.False or JsonValueKind.Null => new(false, $"'{output.Name}' did not pass the run: its rule does not hold."),
            _ => new(false, $"'{output.Name}' cannot judge the run: its rule answered {verdict.GetRawText()}, and a rule answers true or false."),
        };
    }
}
