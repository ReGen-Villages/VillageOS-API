namespace vos.Taproot;

internal static class Subcommands
{
    public static Task RunAsync(string arg, IReadOnlyDictionary<string, Func<string[], Task>> table, Action showUsage)
    {
        if (CommandParser.TryParseSubcommand(arg, out var subcommand, out var args) && table.TryGetValue(subcommand, out var run))
            return run(args);

        showUsage();
        return Task.CompletedTask;
    }
}
