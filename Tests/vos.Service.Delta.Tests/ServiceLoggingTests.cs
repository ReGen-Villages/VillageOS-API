using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

public class ServiceLoggingTests
{
    [Fact]
    public async Task A_service_under_Testing_prints_what_it_logs_to_standard_output()
    {
        var (service, output) = Start("Testing");
        try
        {
            await Settle.UntilAsync(() => Read(output).Contains("VillageOS Delta Service"), "Delta prints its start line");
        }
        finally
        {
            Stop(service);
        }
    }

    [Fact]
    public async Task A_folder_given_at_launch_receives_the_log_file()
    {
        var folder = Directory.CreateTempSubdirectory("delta-logs-");
        var (service, _) = Start("Development", $"--logDirectory={folder.FullName}");
        try
        {
            await Settle.UntilAsync(
                () => folder.EnumerateFiles("delta-*.log").Any(file => file.Length > 0),
                "Delta writes its log into the folder it was given");
        }
        finally
        {
            Stop(service);
            folder.Delete(recursive: true);
        }
    }

    private static (Process Service, StringBuilder Output) Start(string environment, params string[] extraArguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["ASPNETCORE_ENVIRONMENT"] = environment },
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "vos.Service.Delta.dll"));
        start.ArgumentList.Add($"--port={UnusedPort()}");
        start.ArgumentList.Add("--myceliumUrl=http://127.0.0.1:1");
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        var service = Process.Start(start)!;
        var output = new StringBuilder();
        service.OutputDataReceived += (_, line) =>
        {
            if (line.Data is not null)
                lock (output) output.AppendLine(line.Data);
        };
        service.ErrorDataReceived += (_, _) => { };
        service.BeginOutputReadLine();
        service.BeginErrorReadLine();
        return (service, output);
    }

    private static string Read(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    private static int UnusedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static void Stop(Process service)
    {
        service.Kill(entireProcessTree: true);
        service.WaitForExit();
        service.Dispose();
    }
}
