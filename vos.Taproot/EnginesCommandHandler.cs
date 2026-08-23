using System.Text.Json;

namespace vos.Taproot;

/// <summary>
/// The reactive engines' capacity, from the CLI: `engines` prints both engines' totals for the
/// current model, `engines ranges` / `engines rollups` drill in to every reactor with its owner,
/// what it watches or declares, and its footprint.
/// </summary>
public class EnginesCommandHandler
{
    /// <summary>The path a reduction declares when its members are every instance of the related
    /// type, connected to the owner or not — a symbol on the wire, spelled out here.</summary>
    private const string EveryInstance = "*";

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
                .Concat(Names(reactor.GetProperty("WatchedStates")));
            _writer.WriteLine(
                $"    watches: {Watched(watches)}   " +
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
            _writer.WriteLine(
                $"  {reactor.GetProperty("OwnerName").GetString()} · " +
                $"{reactor.GetProperty("PropertyName").GetString()} = {Declaration(reactor)}");
            _writer.WriteLine(
                $"    watches: {Watched(Names(reactor.GetProperty("Watches")))}   " +
                $"members: {reactor.GetProperty("MemberEdges").GetInt32()}   " +
                $"est. {FormatBytes(reactor.GetProperty("EstimatedBytes").GetInt64())}");
        }
    }

    /// <summary>
    /// The platform declares a derived property either as a reduction over the Things a relationship
    /// path reaches or as an expression over property names, and fills only that form's fields —
    /// so rendering the reduction's four regardless prints an expression as a line of blanks.
    /// </summary>
    private static string Declaration(JsonElement reactor)
    {
        if (TextOrNull(reactor, "Expression") is { } expression) return expression;

        var propertyPath = TextOrNull(reactor, "PropertyPath");
        var function = TextOrNull(reactor, "Function");
        var reduction = propertyPath is null ? function : $"{function}({propertyPath})";
        var relatedType = TextOrNull(reactor, "RelatedType");
        var members = TextOrNull(reactor, "Path") is { } path && path != EveryInstance
            ? $"{relatedType} reached by {path}"
            : $"every {relatedType}";
        return $"{reduction} over {members}";
    }

    private static string? TextOrNull(JsonElement reactor, string field) =>
        reactor.GetProperty(field) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static IEnumerable<string> Names(JsonElement array) =>
        array.EnumerateArray().Select(name => name.GetString() ?? "");

    private static string Watched(IEnumerable<string> names)
    {
        var watched = names.ToList();
        return watched.Count > 0 ? string.Join(", ", watched) : "(none)";
    }

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
        _writer.WriteLine("  engines rollups    - Every roll-up reactor: owner, declaration, watches, members, footprint");
    }
}
