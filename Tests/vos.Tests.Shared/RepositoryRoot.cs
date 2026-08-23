namespace vos.Tests.Shared;

/// <summary>
/// The checkout a test is running from: the nearest directory above the test assembly that holds the
/// solution file. It is walked up to rather than reached by a path relative to the test output
/// directory, because how deep that directory sits differs between a run from the solution, from a
/// worktree and from the build agent.
///
/// Reach a file by combining a path onto this rather than searching upward for the file itself: a
/// search has nothing to stop it leaving the worktree it belongs to, and the directory above holds
/// another checkout with the same paths in it.
/// </summary>
public static class RepositoryRoot
{
    public const string SolutionFileName = "VillageOS-API.sln";

    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }
}
