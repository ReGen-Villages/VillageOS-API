using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// The services in a checkout, whatever language each is written in.
///
/// A service that is renamed or deleted leaves its bin/ and obj/ behind, because both are ignored and
/// so survive the commit that removes everything else. Counting directories alone reads what is left
/// as a service, so a guard fails on the machine that once built it while a fresh checkout and the
/// build agent pass. A directory earns the name only by holding something that is not build output.
/// </summary>
internal static class ServiceProjects
{
    public static IEnumerable<DirectoryInfo> Under(string root) =>
        new DirectoryInfo(root)
            .EnumerateDirectories("vos.Service.*")
            .Where(HoldsSomethingOfItsOwn);

    public static IEnumerable<string> NamesUnder(string root) =>
        Under(root).Select(service => service.Name);

    private static bool HoldsSomethingOfItsOwn(DirectoryInfo service) =>
        service.EnumerateFileSystemInfos().Any(entry => !IsBuildOutput(entry.Name));

    private static bool IsBuildOutput(string name) =>
        name.StartsWith('.') || name is "bin" or "obj";
}
