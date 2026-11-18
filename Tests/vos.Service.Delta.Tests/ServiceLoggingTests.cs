using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

public class ServiceLoggingTests
{
    private const string StartLine = "VillageOS Delta Service";

    [Fact]
    public async Task A_service_under_Testing_prints_what_it_logs_to_standard_output()
    {
        var delta = Start("Testing", UnusedPort());
        try
        {
            await UntilOrItExits(delta, () => delta.PrintedItsStartLine, "Delta prints its start line");
        }
        finally
        {
            Stop(delta);
        }
    }

    [Fact]
    public async Task A_folder_given_at_launch_receives_the_log_file()
    {
        var folder = Directory.CreateTempSubdirectory("delta-logs-");
        var delta = Start("Development", UnusedPort(), $"--logDirectory={folder.FullName}");
        try
        {
            await UntilTheLogIsWritten(delta, folder);
        }
        finally
        {
            Stop(delta);
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Bug8414_A_service_that_exits_before_writing_its_log_fails_the_wait_at_once_and_says_how_it_exited()
    {
        var folder = Directory.CreateTempSubdirectory("delta-logs-");
        var delta = Start("Development", port: 0, $"--logDirectory={folder.FullName}");
        try
        {
            var waited = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => UntilTheLogIsWritten(delta, folder));

            Assert.True(waited.Elapsed < Settle.Ceiling / 2,
                $"the wait ended {waited.Elapsed.TotalSeconds:0.#} seconds after Delta was started, not when it exited");
            Assert.Contains("exited with code 1", failure.Message);
        }
        finally
        {
            Stop(delta);
            folder.Delete(recursive: true);
        }
    }

    private static Task UntilTheLogIsWritten(RunningDelta delta, DirectoryInfo folder) =>
        UntilOrItExits(delta,
            () => folder.EnumerateFiles("delta-*.log").Any(file => file.Length > 0),
            "Delta writes its log into the folder it was given",
            () => $"Files in the folder: {FilesIn(folder)}");

    private static string FilesIn(DirectoryInfo folder)
    {
        var files = folder.EnumerateFiles().Select(file => $"{file.Name} ({file.Length} bytes)").ToList();
        return files.Count == 0 ? "none" : string.Join(", ", files);
    }

    private static async Task UntilOrItExits(
        RunningDelta delta, Func<bool> condition, string expectation, Func<string>? alsoSay = null)
    {
        try
        {
            await Settle.UntilAsync(() => condition() || delta.Service.HasExited, expectation);
        }
        catch (TimeoutException timedOut)
        {
            Assert.Fail($"{timedOut.Message}\n{delta.Describe(alsoSay)}");
        }
        if (!condition())
            Assert.Fail($"Delta exited before this was true: {expectation}\n{delta.Describe(alsoSay)}");
    }

    private static RunningDelta Start(string environment, int port, params string[] extraArguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["ASPNETCORE_ENVIRONMENT"] = environment },
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "vos.Service.Delta.dll"));
        start.ArgumentList.Add($"--port={port}");
        start.ArgumentList.Add("--myceliumUrl=http://127.0.0.1:1");
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        var delta = new RunningDelta(Process.Start(start)!);
        delta.Service.OutputDataReceived += (_, line) => delta.Printed(line.Data);
        delta.Service.ErrorDataReceived += (_, line) => delta.Complained(line.Data);
        delta.Service.BeginOutputReadLine();
        delta.Service.BeginErrorReadLine();
        return delta;
    }

    private static int UnusedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static void Stop(RunningDelta delta)
    {
        delta.Service.Kill(entireProcessTree: true);
        delta.Service.WaitForExit();
        delta.Service.Dispose();
    }

    // Only the newest lines of each stream are kept, so a service that loops on an error cannot fill the build log.
    private sealed class RunningDelta(Process service)
    {
        private const int LinesKept = 40;
        private readonly Queue<string> _output = new();
        private readonly Queue<string> _errors = new();
        private readonly Stopwatch _sinceStart = Stopwatch.StartNew();
        private volatile bool _printedItsStartLine;

        public Process Service { get; } = service;

        public bool PrintedItsStartLine => _printedItsStartLine;

        public void Printed(string? line)
        {
            if (line is null) return;
            if (line.Contains(StartLine)) _printedItsStartLine = true;
            Keep(_output, line);
        }

        public void Complained(string? line)
        {
            if (line is not null) Keep(_errors, line);
        }

        public string Describe(Func<string>? alsoSay)
        {
            var seconds = _sinceStart.Elapsed.TotalSeconds;
            string state;
            if (Service.HasExited)
            {
                Service.WaitForExit();
                state = $"Delta exited with code {Service.ExitCode}";
            }
            else state = "Delta is still running";

            return string.Create(CultureInfo.InvariantCulture, $"{state}, {seconds:0.#} seconds after it was started; ")
                + $"its start line {(PrintedItsStartLine ? "reached" : "never reached")} standard output."
                + (alsoSay is null ? "" : $"\n{alsoSay()}")
                + $"\nStandard output, last {LinesKept} lines:\n{Newest(_output)}"
                + $"\nStandard error, last {LinesKept} lines:\n{Newest(_errors)}";
        }

        private static void Keep(Queue<string> lines, string line)
        {
            lock (lines)
            {
                lines.Enqueue(line);
                if (lines.Count > LinesKept) lines.Dequeue();
            }
        }

        private static string Newest(Queue<string> lines)
        {
            lock (lines) return lines.Count == 0 ? "(nothing)" : string.Join("\n", lines);
        }
    }
}
