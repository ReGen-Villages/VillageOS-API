namespace vos.Taproot;

// What every command handler that accepts output options needs: where to write, what the operator
// typed, the broker to ask, a resolver for the names in that text, and whether identifiers are
// printed with their GUIDs.
//
// Options can arrive two ways — passed in by a caller, or written as flags in the argument text —
// and a caller that already asked for GUIDs wins, so a handler another command constructs keeps the
// outer command's choice rather than dropping it because its own text carried no flag. The flags are
// stripped either way, so a handler never has to parse around them.
public abstract class CommandHandlerWithOutputOptions
{
    protected readonly TextWriter _writer;
    protected readonly string _arg;
    protected readonly MyceliumClient _mycelium;
    protected readonly NameResolver _resolver;
    protected readonly OutputOptions _options;

    protected CommandHandlerWithOutputOptions(
        string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options)
    {
        var (parsedOptions, remainingArgs) = OutputOptions.ParseFromArgs(arg);
        var asked = options ?? OutputOptions.Default;
        _options = asked.ShowGuids ? asked : parsedOptions;
        _arg = remainingArgs;
        _writer = writer;
        _mycelium = mycelium;
        _resolver = new NameResolver(mycelium);
    }
}
