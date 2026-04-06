namespace vos.CLI;

/// <summary>
/// Options for controlling CLI output formatting.
/// </summary>
public class OutputOptions
{
    /// <summary>
    /// When true, GUIDs are shown in addition to fully qualified names.
    /// When false (default), only fully qualified names are displayed.
    /// </summary>
    public bool ShowGuids { get; set; }

    /// <summary>
    /// Default output options (names only, no GUIDs).
    /// </summary>
    public static OutputOptions Default => new();

    /// <summary>
    /// Formats an identifier based on current options.
    /// If ShowGuids is true, returns "name (guid)", otherwise just "name".
    /// </summary>
    public string FormatIdentifier(string name, string guid)
    {
        if (string.IsNullOrEmpty(name) || name == "N/A")
            return ShowGuids ? guid : name;

        return ShowGuids ? $"{name} ({guid})" : name;
    }

    /// <summary>
    /// Formats an identifier based on current options.
    /// If ShowGuids is true, returns "name (guid)", otherwise just "name".
    /// </summary>
    public string FormatIdentifier(string name, Guid guid)
    {
        return FormatIdentifier(name, guid.ToString());
    }

    /// <summary>
    /// Parses command arguments and extracts the --showguids flag.
    /// Returns the remaining arguments without the flag.
    /// </summary>
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
