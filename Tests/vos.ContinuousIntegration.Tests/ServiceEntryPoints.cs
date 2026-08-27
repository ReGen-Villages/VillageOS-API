using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// Every service's entry point in the checkout — the files the guards in this project read.
/// </summary>
internal static class ServiceEntryPoints
{
    public static IEnumerable<string> Under(string root) =>
        new DirectoryInfo(root)
            .EnumerateDirectories("vos.Service.*")
            .Select(service => Path.Combine(service.FullName, "Program.cs"))
            .Where(File.Exists);
}
