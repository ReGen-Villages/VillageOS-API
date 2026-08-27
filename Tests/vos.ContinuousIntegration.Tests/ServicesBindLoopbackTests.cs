using System;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// Every service is reachable only through the reverse proxy, which reaches it over loopback. A
/// service that binds any other address is on the network the moment it starts, with whatever
/// authentication it happens to have — so the binding is pinned here rather than trusted.
/// </summary>
public class ServicesBindLoopbackTests
{
    private const string LoopbackBinding = "UseUrls($\"http://localhost:";

    [Fact]
    public void Every_service_entry_point_binds_loopback_and_nothing_else()
    {
        var root = RepositoryRoot.Find();

        var unbound = ServiceEntryPoints.Under(root)
            .Where(entryPoint => !File.ReadAllText(entryPoint).Contains(LoopbackBinding, StringComparison.Ordinal))
            .Select(entryPoint => Path.GetRelativePath(root, entryPoint))
            .ToList();

        Assert.True(unbound.Count == 0,
            "these service entry points do not bind http://localhost explicitly, so they listen wherever "
            + $"the host's defaults point: {string.Join(", ", unbound)}");
    }

    [Fact]
    public void No_service_entry_point_binds_beyond_loopback()
    {
        var root = RepositoryRoot.Find();

        var exposed = ServiceEntryPoints.Under(root)
            .Select(entryPoint => (Path: Path.GetRelativePath(root, entryPoint), Source: File.ReadAllText(entryPoint)))
            .Where(entryPoint => new[] { "0.0.0.0", "[::]", "AnyIP", "ListenAnyIP" }
                .Any(binding => entryPoint.Source.Contains(binding, StringComparison.Ordinal)))
            .Select(entryPoint => entryPoint.Path)
            .ToList();

        Assert.True(exposed.Count == 0,
            $"these service entry points bind beyond loopback: {string.Join(", ", exposed)}");
    }
}
