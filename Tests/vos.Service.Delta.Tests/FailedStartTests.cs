using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

public class FailedStartTests
{
    [Fact]
    public async Task A_service_whose_port_is_taken_exits_with_code_1_and_says_why_on_standard_error()
    {
        using var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        var port = ((IPEndPoint)held.LocalEndpoint).Port;

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["ASPNETCORE_ENVIRONMENT"] = "Testing" },
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "vos.Service.Delta.dll"));
        start.ArgumentList.Add($"--port={port}");
        start.ArgumentList.Add("--myceliumUrl=http://127.0.0.1:1");

        using var service = Process.Start(start)!;
        var standardError = service.StandardError.ReadToEndAsync();
        _ = service.StandardOutput.ReadToEndAsync();
        await Settle.ForAsync(service.WaitForExitAsync(), "Delta exits when its port is taken");

        service.ExitCode.Should().Be(1);
        (await standardError).Should().Contain("Delta could not start").And.Contain("address already in use");
    }
}
