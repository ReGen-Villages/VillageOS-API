using System.Diagnostics;
using System.Net;
using System.Text;
using vos.Service.Shared;
using vos.Service.Xylem.Services;
using vos.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class ModelIngestRunnerTests
{
    private const string MyceliumUrl = "http://localhost:5000";

    private static ServiceCredential Credential(
        string? serviceToken = null, string? apiKey = null, string? mintedToken = null)
    {
        var handler = new MockHttpMessageHandler(_ => mintedToken is null
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"token\":\"{mintedToken}\"}}", Encoding.UTF8, "application/json")
            });

        return new ServiceCredential(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl,
            serviceToken, apiKey);
    }

    private static ModelIngestRunner Runner(ServiceCredential credential, string dll = "/tools/ModelIngest.dll") =>
        new(dll, MyceliumUrl, credential, NullLogger<ModelIngestRunner>.Instance);

    private static ModelIngestRunner Runner(ILogger<ModelIngestRunner> log) =>
        new("/tools/ModelIngest.dll", MyceliumUrl, Credential(), log);

    [Fact]
    public async Task RunAsync_missing_tool_reports_a_clear_error_without_spawning()
    {
        var runner = Runner(Credential(serviceToken: "tok"), dll: "/no/such/ModelIngest.dll");

        var r = await runner.RunAsync("/tmp/whatever.ifc", "Demo", default);

        r.Success.Should().BeFalse();
        r.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task A_tool_file_that_dotnet_cannot_run_is_a_failed_ingest_carrying_what_dotnet_wrote()
    {
        var notAnAssembly = Path.Combine(Path.GetTempPath(), $"not-an-assembly-{Guid.NewGuid():N}.dll");
        await File.WriteAllTextAsync(notAnAssembly, "not an assembly");
        try
        {
            using var hangDetector = new CancellationTokenSource(Settle.Ceiling);

            var result = await Runner(Credential(serviceToken: "tok"), dll: notAnAssembly)
                .RunAsync("/tmp/whatever.ifc", "Demo", hangDetector.Token);

            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNullOrWhiteSpace().And.NotBe("IFC ingest failed.");
        }
        finally
        {
            File.Delete(notAnAssembly);
        }
    }

    [Fact]
    public async Task The_ingest_process_is_handed_its_token_through_the_environment()
    {
        var startInfo = await Runner(Credential(serviceToken: "the.service.jwt"))
            .BuildStartInfoAsync("/tmp/model.ifc", "Demo", "/tmp/result.json", default);

        startInfo.Environment["Token"].Should().Be("the.service.jwt");
        startInfo.ArgumentList.Should().NotContain("--token").And.NotContain("the.service.jwt");
    }

    // The ingest tool authenticates with this setting and nothing else, so a service holding a key rather
    // than a token would launch it with no credential at all.
    [Fact]
    public async Task A_held_api_key_reaches_the_ingest_process_as_the_token_it_was_exchanged_for()
    {
        var minted = TestTokens.For(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));

        var startInfo = await Runner(Credential(apiKey: "key-1", mintedToken: minted))
            .BuildStartInfoAsync("/tmp/model.ifc", "Demo", "/tmp/result.json", default);

        startInfo.Environment["Token"].Should().Be(minted);
    }

    [Fact]
    public async Task An_absent_token_leaves_the_ingest_process_without_one()
    {
        var startInfo = await Runner(Credential()).BuildStartInfoAsync("/tmp/model.ifc", "Demo", "/tmp/result.json", default);

        startInfo.Environment.Should().NotContainKey("Token");
    }

    // The operating system holds a limited amount of a child's unread output, 65,536 bytes where this was
    // measured. A megabyte is past that on any system, so a child that writes it to a stream nobody is
    // reading waits, and a reader waiting for the other stream to end waits with it.
    [FactNeedingAShell]
    public async Task A_megabyte_of_error_output_is_read_while_standard_output_is_still_open()
    {
        var startInfo = AShellRunning("head -c 1048576 /dev/zero | tr '\\0' 'e' >&2; echo done; exit 3");
        using var hangDetector = new CancellationTokenSource(Settle.Ceiling);

        var run = await ModelIngestRunner.RunToExitAsync(startInfo, hangDetector.Token);

        run!.ExitCode.Should().Be(3);
        run.StandardOutput.Trim().Should().Be("done");
        run.StandardError.Should().HaveLength(1048576);
    }

    private static ProcessStartInfo AShellRunning(string script)
    {
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);
        return startInfo;
    }

    // The ingest tool, in another repository, has a test of its own that runs it with these arguments.
    [Fact]
    public async Task The_ingest_process_is_started_with_the_path_its_result_file_goes_to()
    {
        var startInfo = await Runner(Credential())
            .BuildStartInfoAsync("/tmp/model.ifc", "Demo", "/tmp/result.json", default);

        startInfo.ArgumentList.Should().Equal(
            "/tools/ModelIngest.dll", "--ifc", "/tmp/model.ifc", "--post", MyceliumUrl, "--name", "Demo",
            "--profile", "analysis", "--result", "/tmp/result.json");
    }

    // Copied from two runs of the ingest tool posting one building model into one model: a sample written
    // by hand proves only that the reader reads the sample.
    private const string ResultFileOfAFirstUpload =
        """{"thingsCreated":9,"thingsUpdated":0,"relationshipsCreated":4}""";

    private const string ResultFileOfTheSameUploadAgain =
        """{"thingsCreated":0,"thingsUpdated":9,"relationshipsCreated":0}""";

    private static ModelIngestRunner.ProcessRun ARunThatExitedWith(int code, string errorOutput = "") =>
        new(code, "", errorOutput);

    [Theory]
    [InlineData(ResultFileOfAFirstUpload, 9, 0, 4)]
    [InlineData(ResultFileOfTheSameUploadAgain, 0, 9, 0)]
    public void An_upload_reports_what_the_result_file_says_the_broker_created_and_updated(
        string resultFile, int created, int updated, int relationshipsCreated)
    {
        var log = new CapturingLogger<ModelIngestRunner>();

        var result = Runner(log).ResultOf(ARunThatExitedWith(0), resultFile);

        result.Should().Be(new IngestRunResult(true, created, updated, relationshipsCreated, null));
        log.Lines.Should().BeEmpty();
    }

    // The model was written, so the run stays a success. Nought would say the model gained nothing.
    [Theory]
    [InlineData(null, "none was written")]
    [InlineData("""{"thingsCreated":9,"thingsUpdated":0}""", """{"thingsCreated":9,"thingsUpdated":0}""")]
    [InlineData("not a result", "not a result")]
    [InlineData("null", "null")]
    public void A_successful_run_with_no_result_file_it_can_read_carries_no_counts_and_is_logged(
        string? resultFile, string theLogSaysOfTheFile)
    {
        var log = new CapturingLogger<ModelIngestRunner>();

        var result = Runner(log).ResultOf(
            ARunThatExitedWith(0, errorOutput: "The broker accepted the post, but a reply carried no counts.\n"),
            resultFile);

        result.Should().Be(new IngestRunResult(true, null, null, null, null));
        log.Lines.Should().ContainSingle()
            .Which.Should().Contain(theLogSaysOfTheFile)
            .And.Contain("The broker accepted the post, but a reply carried no counts.");
    }

    [FactNeedingAShell]
    public async Task The_result_file_a_child_process_wrote_is_read_and_then_removed()
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"result-{Guid.NewGuid():N}.json");
        var startInfo = AShellRunning($"printf '%s' '{ResultFileOfTheSameUploadAgain}' > '{resultPath}'");
        using var hangDetector = new CancellationTokenSource(Settle.Ceiling);

        var result = await Runner(Credential()).RunAndReadResultAsync(startInfo, resultPath, hangDetector.Token);

        result.Should().Be(new IngestRunResult(true, 0, 9, 0, null));
        File.Exists(resultPath).Should().BeFalse();
    }

    [Fact]
    public void A_failed_run_answers_with_what_the_tool_wrote_to_its_error_output()
    {
        var result = Runner(new CapturingLogger<ModelIngestRunner>()).ResultOf(
            ARunThatExitedWith(1, errorOutput: "  The broker refused the post.\n"), resultFile: null);

        result.Should().Be(new IngestRunResult(false, 0, 0, 0, "The broker refused the post."));
    }

    [Fact]
    public void A_failed_run_that_wrote_no_error_output_still_answers_with_a_reason()
    {
        var result = Runner(new CapturingLogger<ModelIngestRunner>()).ResultOf(
            ARunThatExitedWith(1, errorOutput: " \n"), resultFile: null);

        result.Should().Be(new IngestRunResult(false, 0, 0, 0, "IFC ingest failed."));
    }
}
