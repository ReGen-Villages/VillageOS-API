using System.Text.Json;

namespace vos.Taproot;

// Repoint a Thing's type to a different archetype. Multiple inheritance is a first-class feature, so this
// changes only ONE type edge, never collapses the rest:
//   retype <thing> <new>         — swap when the Thing has 0 or 1 type; refuses (asks for <old>) if it has many
//   retype <thing> <old> <new>   — replace only the <old> type edge, leaving the Thing's other is-edges intact
// Uses existing relationship APIs — no broker change.
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
            var isPred = await _resolver.ResolveThingAsync("is");
            if (!isPred.IsSuccess) { _writer.WriteLine("Error: no 'is' predicate found in the model."); return; }
            var newType = await _resolver.ResolveThingAsync(tok[^1]);
            if (!newType.IsSuccess) { _writer.WriteLine($"Error: {newType.ErrorMessage}"); return; }

            var (thingId, isId, newTypeId) = (thing.Id, isPred.Id, newType.Id);

            // The Thing's current type edges: (relationshipId, targetArchetypeId).
            var typeEdges = new List<(Guid RelId, Guid Target)>();
            var rels = await _mycelium.GetAllRelationshipsAsync();
            if (rels.ValueKind == JsonValueKind.Array)
                foreach (var rel in rels.EnumerateArray())
                    if (Guid.TryParse(rel.GetStringOrDefault("SubjectId"), out var s) && s == thingId
                        && Guid.TryParse(rel.GetStringOrDefault("PredicateId"), out var p) && p == isId
                        && Guid.TryParse(rel.GetStringOrDefault("Id"), out var rid)
                        && Guid.TryParse(rel.GetStringOrDefault("TargetId"), out var tid))
                        typeEdges.Add((rid, tid));

            List<(Guid RelId, Guid Target)> toRemove;
            if (tok.Length >= 3)
            {
                // retype <thing> <old> <new>: replace only the named type, keep the others.
                var oldType = await _resolver.ResolveThingAsync(tok[1]);
                if (!oldType.IsSuccess) { _writer.WriteLine($"Error: {oldType.ErrorMessage}"); return; }
                toRemove = typeEdges.Where(e => e.Target == oldType.Id).ToList();
            }
            else if (typeEdges.Count > 1)
            {
                _writer.WriteLine($"Error: '{tok[0]}' has multiple types ({typeEdges.Count}); specify which to change: retype <thing> <old> <new>.");
                return;
            }
            else
            {
                // 0 or 1 existing type — swap the single one (or just add if untyped).
                toRemove = typeEdges;
            }

            foreach (var e in toRemove) await _mycelium.DeleteRelationshipAsync(e.RelId);
            await _mycelium.CreateRelationshipAsync(thingId, isId, newTypeId);
            _writer.WriteLine($"Retyped {tok[0]} as {tok[^1]} (replaced {toRemove.Count} type edge(s)).");
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
        }
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: retype <thing> <new-archetype>");
        _writer.WriteLine("       retype <thing> <old-archetype> <new-archetype>   (when the Thing has multiple types)");
        _writer.WriteLine("  Changes one is-edge to a different archetype; a Thing's other types are left intact.");
    }
}
