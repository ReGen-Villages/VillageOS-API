using System.Text.Json;

namespace vos.Taproot;

/// <summary>
/// The reactive engines' capacity, from the CLI: `engines` prints both engines' totals for the
/// current model, `engines ranges` / `engines rollups` drill in to every reactor with its owner,
/// what it watches or declares, and its footprint.
/// </summary>
public class EnginesCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public EnginesCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (tok.Length == 0 ? "" : tok[0])
            {
                case "":
                    await ShowTotalsAsync();
                    break;
                case "ranges":
                    await ListRangeReactorsAsync();
                    break;
                case "rollups":
                    await ListRollupReactorsAsync();
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private async Task ShowTotalsAsync()
    {
        var summary = await _mycelium.GetEngineMetricsAsync();
        var ranges = summary.GetProperty("Ranges");
        var rollups = summary.GetProperty("Rollups");

        _writer.WriteLine($"Reactive engines — {summary.GetProperty("ModelName").GetString()}");
        _writer.WriteLine(
            $"  Range evaluation      {ranges.GetProperty("RegisteredRanges").GetInt32(),6} ranges    " +
            $"{ranges.GetProperty("DependencyEdges").GetInt32(),6} edges      " +
            FormatBytes(ranges.GetProperty("EstimatedBytes").GetInt64()));
        _writer.WriteLine(
            $"  Reactive computation  {rollups.GetProperty("RollupProperties").GetInt32(),6} roll-ups  " +
            $"{rollups.GetProperty("MemberEdges").GetInt32(),6} members    " +
            FormatBytes(rollups.GetProperty("EstimatedBytes").GetInt64()));
        _writer.WriteLine(
            $"  Total est. memory     {FormatBytes(summary.GetProperty("EstimatedBytesTotal").GetInt64())}");
        _writer.WriteLine();
        _writer.WriteLine("Drill in: engines ranges | engines rollups");
    }

    private async Task ListRangeReactorsAsync()
    {
        var reactors = (await _mycelium.GetEngineReactorsAsync()).GetProperty("Ranges");
        if (reactors.GetArrayLength() == 0)
        {
            _writer.WriteLine("No range reactors in the current model.");
            return;
        }

        _writer.WriteLine($"Range reactors ({reactors.GetArrayLength()})");
        foreach (var reactor in reactors.EnumerateArray())
        {
            _writer.WriteLine($"  {reactor.GetProperty("OwnerName").GetString()} · {reactor.GetProperty("RangeName").GetString()}");
            var watches = Names(reactor.GetProperty("WatchedProperties"))
                .Concat(Names(reactor.GetProperty("WatchedStates")))
                .ToList();
            _writer.WriteLine(
                $"    watches: {(watches.Count > 0 ? string.Join(", ", watches) : "(none)")}   " +
                $"edges: {reactor.GetProperty("DependencyEdges").GetInt32()} (+{reactor.GetProperty("BindingEdges").GetInt32()} binding)   " +
                $"est. {FormatBytes(reactor.GetProperty("EstimatedBytes").GetInt64())}");
        }
    }

    private async Task ListRollupReactorsAsync()
    {
        var reactors = (await _mycelium.GetEngineReactorsAsync()).GetProperty("Rollups");
        if (reactors.GetArrayLength() == 0)
        {
            _writer.WriteLine("No roll-up reactors in the current model.");
            return;
        }

        _writer.WriteLine($"Roll-up reactors ({reactors.GetArrayLength()})");
        foreach (var reactor in reactors.EnumerateArray())
        {
            var path = reactor.GetProperty("PropertyPath").ValueKind == JsonValueKind.String
                ? reactor.GetProperty("PropertyPath").GetString() : null;
            var reduction = path is null
                ? reactor.GetProperty("Function").GetString()
                : $"{reactor.GetProperty("Function").GetString()}({path})";
            _writer.WriteLine(
                $"  {reactor.GetProperty("OwnerName").GetString()} · {reactor.GetProperty("PropertyName").GetString()} = " +
                $"{reduction} over {reactor.GetProperty("Direction").GetString()} {reactor.GetProperty("Predicate").GetString()} " +
                $"from {reactor.GetProperty("RelatedType").GetString()}");
            _writer.WriteLine(
                $"    members: {reactor.GetProperty("MemberEdges").GetInt32()}   " +
                $"est. {FormatBytes(reactor.GetProperty("EstimatedBytes").GetInt64())}");
        }
    }

    private static IEnumerable<string> Names(JsonElement array) =>
        array.EnumerateArray().Select(name => name.GetString() ?? "");

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        var unit = "B";
        foreach (var next in new[] { "KB", "MB", "GB", "TB" })
        {
            if (value < 1024) break;
            value /= 1024;
            unit = next;
        }
        return $"{value:F1} {unit}";
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  engines            - Both engines' totals for the current model");
        _writer.WriteLine("  engines ranges     - Every range reactor: owner, watches, edges, footprint");
        _writer.WriteLine("  engines rollups    - Every roll-up reactor: owner, definition, members, footprint");
    }
}
