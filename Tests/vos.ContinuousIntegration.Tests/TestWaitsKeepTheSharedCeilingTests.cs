using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The build agent is shared, and a test that stops waiting after a few seconds has failed there on
// correct code more than once. Settle holds the one ceiling a wait may keep, and nothing but a
// reading of the source stops the next test from writing a shorter clock of its own.
public class TestWaitsKeepTheSharedCeilingTests
{
    private static readonly string Root = RepositoryRoot.Find();

    // Settle is where the clocks live, and this file writes waits out as examples to judge.
    private static readonly string[] NotJudged = ["Settle.cs", "TestWaitsKeepTheSharedCeilingTests.cs"];

    [Fact]
    public void No_test_puts_its_own_clock_on_a_wait()
    {
        var keepingTheirOwn =
            from file in TestSourceFiles()
            from wait in ClockedWaits.In(File.ReadAllText(file))
            where ClockedWaits.KeepsItsOwnClock(wait)
            select $"{Path.GetRelativePath(Root, file)}:{wait.Line} {wait.Call}";

        var found = keepingTheirOwn.ToList();

        Assert.True(found.Count == 0,
            "these waits carry their own clock. Wait for the condition with Settle.UntilAsync or for the "
            + "task with Settle.ForAsync, put Settle.Ceiling on a wait that needs a clock, and pause with "
            + "Settle.BeforeAssertingAbsenceAsync only before asserting that something did not happen:\n"
            + string.Join("\n", found));
    }

    // The solution is what the build runs, so it is the list this sweep cannot be short of: a test
    // project the walk does not reach would go on writing its own clocks with this guard green.
    [Fact]
    public void The_sweep_reads_every_test_project_the_solution_lists()
    {
        var solution = File.ReadAllText(Path.Combine(Root, RepositoryRoot.SolutionFileName));
        var listed = Regex.Matches(solution, @"""(?<project>[^""]+Tests\.csproj)""")
            .Select(match => Path.GetDirectoryName(match.Groups["project"].Value.Replace('\\', Path.DirectorySeparatorChar))!)
            .ToList();
        var read = TestProjectDirectories(new DirectoryInfo(Root))
            .Select(directory => Path.GetRelativePath(Root, directory.FullName))
            .ToList();

        Assert.NotEmpty(listed);
        Assert.Empty(listed.Except(read));
    }

    [Fact]
    public void The_sweep_finds_waits_in_the_files_it_reads()
    {
        Assert.Contains(TestSourceFiles(), file => ClockedWaits.In(File.ReadAllText(file)).Count > 0);
    }

    [Theory]
    [InlineData("await Task.Delay(150);")]
    [InlineData("await Task.Delay(TimeSpan.FromSeconds(5));")]
    [InlineData("await Task.Delay(pause, cancellationToken);")]
    [InlineData("var first = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(5)));")]
    [InlineData("Thread.Sleep(20);")]
    [InlineData("using var window = new CancellationTokenSource(TimeSpan.FromSeconds(5));")]
    [InlineData("using var window = new CancellationTokenSource(5000);")]
    [InlineData("window.CancelAfter(TimeSpan.FromSeconds(5));")]
    [InlineData("await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));")]
    [InlineData("started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();")]
    [InlineData("started.Wait(10_000);")]
    [InlineData("started.Wait(millisecondsTimeout: 10_000);")]
    [InlineData("signalled.WaitOne(TimeSpan.FromSeconds(5));")]
    [InlineData("Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(5));")]
    [InlineData("Task.WaitAny(new[] { first, second }, 5000);")]
    [InlineData("SpinWait.SpinUntil(() => fired, TimeSpan.FromSeconds(5));")]
    public void A_wait_with_a_clock_of_its_own_is_refused(string source)
    {
        Assert.Contains(ClockedWaits.In(source), ClockedWaits.KeepsItsOwnClock);
    }

    [Theory]
    [InlineData("await Task.Delay(Timeout.Infinite, token);")]
    [InlineData("var first = await Task.WhenAny(arrived.Task, Task.Delay(Settle.Ceiling));")]
    [InlineData("using var window = new CancellationTokenSource(Settle.Ceiling);")]
    [InlineData("using var stop = new CancellationTokenSource();")]
    [InlineData("await registered.Task.WaitAsync(Settle.Ceiling);")]
    [InlineData("await registered.Task.WaitAsync(cancellationToken);")]
    [InlineData("released.Wait();")]
    [InlineData("signalled.WaitOne();")]
    [InlineData("Task.WaitAll(first, second);")]
    [InlineData("Task.WaitAll(new[] { first, second }, Settle.Ceiling);")]
    public void A_wait_on_the_shared_ceiling_or_with_no_clock_is_accepted(string source)
    {
        var waits = ClockedWaits.In(source);

        Assert.NotEmpty(waits);
        Assert.DoesNotContain(waits, ClockedWaits.KeepsItsOwnClock);
    }

    [Fact]
    public void A_wait_written_in_a_comment_is_not_a_wait()
    {
        Assert.Empty(ClockedWaits.In("// Use it wherever the alternative is await Task.Delay(50)."));
    }

    [Fact]
    public void A_wait_after_an_address_on_the_same_line_is_still_read()
    {
        const string source = "var client = Client(\"http://localhost\"); await Task.Delay(50);";

        Assert.Contains(ClockedWaits.In(source), ClockedWaits.KeepsItsOwnClock);
    }

    [Fact]
    public void A_call_the_source_never_closes_is_not_read_as_a_wait()
    {
        Assert.Empty(ClockedWaits.In("await Task.Delay(TimeSpan.FromSeconds(5"));
    }

    [Fact]
    public void Each_wait_is_judged_on_its_own_clock_and_reported_on_its_own_line()
    {
        const string source = """
            using var window = new CancellationTokenSource(Settle.Ceiling);
            await Task.Delay(Math.Max(1, 2), window.Token);
            """;

        var refused = Assert.Single(ClockedWaits.In(source), ClockedWaits.KeepsItsOwnClock);

        Assert.Equal(2, refused.Line);
        Assert.Equal("Task.Delay(Math.Max(1, 2), window.Token)", refused.Call);
    }

    // Hidden directories are skipped because a git worktree lives under one and holds a second
    // checkout of every file this would otherwise read.
    private static IEnumerable<string> TestSourceFiles() =>
        TestProjectDirectories(new DirectoryInfo(Root))
            .SelectMany(project => project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            .Where(file => !NotJudged.Contains(file.Name) && !IsBuildOutput(file))
            .Select(file => file.FullName);

    private static IEnumerable<DirectoryInfo> TestProjectDirectories(DirectoryInfo directory)
    {
        if (directory.Name.EndsWith(".Tests", StringComparison.Ordinal)
            || directory.Name.EndsWith(".Tests.Shared", StringComparison.Ordinal))
        {
            yield return directory;
            yield break;
        }

        var searchable = directory.EnumerateDirectories()
            .Where(child => !child.Name.StartsWith('.') && child.Name is not ("bin" or "obj" or "node_modules"));

        foreach (var child in searchable)
        foreach (var project in TestProjectDirectories(child))
            yield return project;
    }

    private static bool IsBuildOutput(FileInfo file) =>
        Path.GetRelativePath(Root, file.FullName).Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
}
