using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// The services in the checkout written in C#, and the entry-point files the guards in this project
/// read. A service in another language has no Program.cs and so appears in neither.
/// </summary>
internal static class ServiceEntryPoints
{
    private const string EntryPointFileName = "Program.cs";

    public static IEnumerable<DirectoryInfo> Services(string root) =>
        ServiceProjects.Under(root).Where(service => File.Exists(EntryPointIn(service)));

    public static IEnumerable<string> Under(string root) =>
        Services(root).Select(EntryPointIn);

    private static string EntryPointIn(DirectoryInfo service) =>
        Path.Combine(service.FullName, EntryPointFileName);
}
