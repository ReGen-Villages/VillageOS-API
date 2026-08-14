namespace vos.Taproot;

// Rename a Thing in place, keeping its Id and all edges:
//   rename <thing> <new-name>
// The <thing> is resolved by name or id; the new name is the rest of the argument (so it may contain
// spaces). Uses the broker's PUT /api/things/{id}/name — no delete+recreate, so relationships survive.
public class RenameCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;
    private readonly NameResolver _resolver;

    public RenameCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg ?? string.Empty;
        _writer = writer;
        _mycelium = mycelium;
        _resolver = new NameResolver(mycelium);
    }

    public async Task ExecuteAsync()
    {
        // Split off the first token as the target; the remainder is the new name (may contain spaces).
        var trimmed = _arg.Trim();
        var space = trimmed.IndexOf(' ');
        if (space < 0) { ShowUsage(); return; }

        var target = trimmed[..space];
        var newName = trimmed[(space + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(newName)) { ShowUsage(); return; }

        try
        {
            var thing = await _resolver.ResolveThingAsync(target);
            if (!thing.IsSuccess) { _writer.WriteLine($"Error: {thing.ErrorMessage}"); return; }

            if (await _mycelium.RenameThingAsync(thing.Id, newName))
                _writer.WriteLine($"Renamed {target} to {newName}.");
            else
                _writer.WriteLine($"Error: rename failed for '{target}'.");
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
        }
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: rename <thing> <new-name>");
        _writer.WriteLine("  Renames a Thing in place, keeping its Id and all edges. The new name may contain spaces.");
    }
}
