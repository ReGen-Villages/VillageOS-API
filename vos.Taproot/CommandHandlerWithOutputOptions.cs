namespace vos.Taproot;

// The output flags are stripped from the argument text, so a handler never has to parse around them.
public abstract class CommandHandlerWithOutputOptions
{
    protected readonly TextWriter _writer;
    protected readonly string _arg;
    protected readonly MyceliumClient _mycelium;
    protected readonly NameResolver _resolver;
    protected readonly OutputOptions _options;

    protected CommandHandlerWithOutputOptions(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        (_options, _arg) = OutputOptions.ParseFromArgs(arg);
        _writer = writer;
        _mycelium = mycelium;
        _resolver = new NameResolver(mycelium);
    }
}
