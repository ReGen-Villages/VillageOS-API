using System.Text.Json;

namespace vos.Taproot;

// `retype <thing> <new-archetype>` repoints a Thing's `is`-edge to a different archetype in one action:
// remove its current is-edge(s) and add is -> new-archetype. The human-in-the-loop reclassification
// (e.g. a mis-classified proxy becomes a SolarArray, which changes what roll-ups/analysis pick it up),
// using existing relationship APIs — no broker change.
public class RetypeCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;
    private readonly NameResolver _resolver;

    public RetypeCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg ?? string.Empty;
        _writer = writer;
        _mycelium = mycelium;
        _resolver = new NameResolver(mycelium);
    }

    public async Task ExecuteAsync()
    {
        var tok = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 2) { ShowUsage(); return; }

        try
        {
            var thing = await _resolver.ResolveThingAsync(tok[0]);
            if (!thing.IsSuccess) { _writer.WriteLine($"Error: {thing.ErrorMessage}"); return; }
            var archetype = await _resolver.ResolveThingAsync(tok[1]);
            if (!archetype.IsSuccess) { _writer.WriteLine($"Error: {archetype.ErrorMessage}"); return; }
            var isPred = await _resolver.ResolveThingAsync("is");
            if (!isPred.IsSuccess) { _writer.WriteLine("Error: no 'is' predicate found in the model."); return; }

            var (thingId, newTypeId, isId) = (thing.Id, archetype.Id, isPred.Id);

            // Remove the Thing's current type edges, then add the new one — leaving exactly one is-edge.
            var removed = 0;
            var rels = await _mycelium.GetAllRelationshipsAsync();
            if (rels.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (!Guid.TryParse(rel.GetStringOrDefault("SubjectId"), out var subj) || subj != thingId) continue;
                    if (!Guid.TryParse(rel.GetStringOrDefault("PredicateId"), out var pred) || pred != isId) continue;
                    if (Guid.TryParse(rel.GetStringOrDefault("Id"), out var relId))
                    {
                        await _mycelium.DeleteRelationshipAsync(relId);
                        removed++;
                    }
                }
            }

            await _mycelium.CreateRelationshipAsync(thingId, isId, newTypeId);
            _writer.WriteLine($"Retyped {tok[0]} as {tok[1]} (removed {removed} previous type edge(s)).");
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {ex.Message}");
        }
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: retype <thing> <new-archetype>");
        _writer.WriteLine("  Repoints the Thing's is-edge to a different archetype (removes the old is-edge, adds the new).");
    }
}
