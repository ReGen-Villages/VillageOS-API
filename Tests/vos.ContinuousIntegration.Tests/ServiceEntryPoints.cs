namespace vos.ContinuousIntegration.Tests;

// The services in the checkout written in C#, and the entry-point files the guards in this project
// read. A service in another language has no Program.cs and so appears in neither.
internal static class ServiceEntryPoints
{
    private const string EntryPointFileName = "Program.cs";
    private const string SharedNodeHostCall = "DagNodeHost.Run";
    private static readonly string SharedNodeHostFile = Path.Combine("vos.Service.Shared", "DagNode", "DagNodeHost.cs");

    public static IEnumerable<DirectoryInfo> Services(string root) =>
        ServiceProjects.Under(root).Where(service => File.Exists(EntryPointIn(service)));

    public static IEnumerable<string> Under(string root) =>
        Services(root).Select(EntryPointIn);

    public static string SourceOf(string root, string entryPoint)
    {
        var source = File.ReadAllText(entryPoint);
        return source.Contains(SharedNodeHostCall, StringComparison.Ordinal)
            ? source + Environment.NewLine + File.ReadAllText(Path.Combine(root, SharedNodeHostFile))
            : source;
    }

    private static string EntryPointIn(DirectoryInfo service) =>
        Path.Combine(service.FullName, EntryPointFileName);
}
