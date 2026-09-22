namespace vos.Taproot;

public class OutputOptions
{
    public bool ShowGuids { get; set; }

    public static OutputOptions Default => new();

    public string FormatIdentifier(string name, string guid)
    {
        if (string.IsNullOrEmpty(name) || name == "N/A")
            return ShowGuids ? guid : name;

        return ShowGuids ? $"{name} ({guid})" : name;
    }

    public string FormatIdentifier(string name, Guid guid)
    {
        return FormatIdentifier(name, guid.ToString());
    }

    public static (OutputOptions Options, string RemainingArgs) ParseFromArgs(string args)
    {
        var options = new OutputOptions();
        if (string.IsNullOrEmpty(args))
            return (options, args);

        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var showGuidsIndex = tokens.FindIndex(t =>
            t.Equals("--showguids", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("-g", StringComparison.OrdinalIgnoreCase));

        if (showGuidsIndex >= 0)
        {
            options.ShowGuids = true;
            tokens.RemoveAt(showGuidsIndex);
        }

        return (options, string.Join(" ", tokens));
    }
}
