using Microsoft.Extensions.Configuration;

namespace vos.Tests.Shared;

public static class LaunchFlags
{
    public const int Port = 7111;
    public const string MyceliumUrl = "https://localhost:7243";

    public static readonly string[] Required = [$"--port={Port}", $"--myceliumUrl={MyceliumUrl}"];

    public static string[] With(params string[] extra) => [.. Required, .. extra];

    public static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();
}
