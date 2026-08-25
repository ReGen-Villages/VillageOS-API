using System.Collections.Generic;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// Every service is reachable only through the reverse proxy, which reaches it over loopback. A service
/// that binds any other address is on the network the moment it starts, with whatever authentication it
/// happens to have — so the binding is pinned here rather than trusted.
///
/// Two questions, asked as widely as each can be answered. What an entry point binds is read from the
/// binding calls themselves, which only the managed services can be read for. Whether an entry point
/// names an address reaching past the machine is asked of every service in every language, because that
/// is the answer that matters and it needs no parsing.
/// </summary>
public class ServicesBindLoopbackTests
{
    /// <summary>The file that decides what a service listens on, per language.</summary>
    private static readonly string[] EntryPointFiles =
        ["Program.cs", "main.go", Path.Combine("src", "index.ts"), "app.py", Path.Combine("src", "main.rs")];

    [Fact]
    public void Every_managed_service_states_a_binding_and_every_binding_it_states_is_loopback()
    {
        var root = RepositoryRoot.Find();

        var wrong = ManagedEntryPoints(root)
            .Select(entryPoint => (Path: Path.GetRelativePath(root, entryPoint), Calls: ServiceBindings.BindingCallsIn(File.ReadAllText(entryPoint))))
            .Where(entryPoint => entryPoint.Calls.Count == 0 || !entryPoint.Calls.All(ServiceBindings.IsLoopback))
            .Select(entryPoint => entryPoint.Path)
            .ToList();

        Assert.True(wrong.Count == 0,
            "these service entry points either state no binding, so they listen wherever the host's "
            + $"defaults point, or state one that reaches past this machine: {string.Join(", ", wrong)}");
    }

    [Fact]
    public void No_service_in_any_language_names_an_address_beyond_loopback()
    {
        var root = RepositoryRoot.Find();

        var exposed = EntryPoints(root)
            .Where(entryPoint => ServiceBindings.ReachesBeyondLoopback(File.ReadAllText(entryPoint)))
            .Select(entryPoint => Path.GetRelativePath(root, entryPoint))
            .ToList();

        Assert.True(exposed.Count == 0,
            $"these service entry points name an address beyond loopback: {string.Join(", ", exposed)}");
    }

    // Without this the two tests above pass by finding nothing, which is how a guard stops guarding
    // without anyone noticing — a service moved or a directory renamed leaves them green.
    [Fact]
    public void The_sweep_finds_a_service_in_every_language_the_repository_carries()
    {
        var root = RepositoryRoot.Find();

        var found = EntryPoints(root).Select(Path.GetExtension).Distinct().OrderBy(extension => extension);

        Assert.Equal([".cs", ".go", ".py", ".rs", ".ts"], found);
    }

    private static IEnumerable<string> ManagedEntryPoints(string root) =>
        EntryPoints(root).Where(entryPoint => Path.GetExtension(entryPoint) == ".cs");

    private static IEnumerable<string> EntryPoints(string root) =>
        new DirectoryInfo(root)
            .EnumerateDirectories("vos.Service.*")
            .SelectMany(service => EntryPointFiles.Select(file => Path.Combine(service.FullName, file)))
            .Where(File.Exists);
}
