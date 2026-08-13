using Microsoft.Extensions.Configuration;

namespace vos.Service.Shared.Configuration;

// Reads one launch setting, taking the command line first and configuration second.
public sealed class LaunchSettingReader
{
    private readonly string[] _arguments;
    private readonly IConfiguration? _configuration;

    public LaunchSettingReader(string[]? arguments, IConfiguration? configuration = null)
    {
        _arguments = arguments ?? [];
        _configuration = configuration;
    }

    public string? Read(string flagName)
    {
        var prefix = $"--{flagName}=";

        foreach (var argument in _arguments)
        {
            // Ordinal, because culture-sensitive matching ignores invisible characters such as a
            // soft hyphen. It would accept "--po<soft hyphen>rt=" as the port flag and then cut the
            // value one character short, leaving part of the flag inside it.
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
                return argument[prefix.Length..];
        }

        return _configuration?[ConfigurationKeyFor(flagName)];
    }

    // A credential is read from configuration alone. Command-line arguments are visible to every
    // process on the host and to anything that records the line a service was started with.
    public string? ReadCredential(string settingName) =>
        _configuration?[ConfigurationKeyFor(settingName)];

    // Flags are camel case on the command line and Pascal case in configuration: --myceliumUrl reads MyceliumUrl.
    private static string ConfigurationKeyFor(string flagName) =>
        char.ToUpperInvariant(flagName[0]) + flagName[1..];
}
