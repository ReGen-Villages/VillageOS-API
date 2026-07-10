using vos.ManagedMicroservice.Xylem.Services;
using Xunit;
using FluentAssertions;

namespace vos.ManagedMicroservice.Xylem.Tests;

// Covers the ingest orchestration (US #5842): validate, clear the model for new-model, run IfcIngest via
// the runner, surface the counts — independent of the subprocess/HTTP details.
public class IngestHandlerTests : IDisposable
{
    private sealed class FakeRunner : IIfcIngestRunner
    {
        public string? SeenPath, SeenName;
        public bool FileExistedAtCall;
        public IngestRunResult Result = new(true, 3, 1, 2, null);
        public int Calls;

        public Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct)
        {
            Calls++;
            SeenPath = ifcPath; SeenName = modelName;
            FileExistedAtCall = File.Exists(ifcPath);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePreparer : IModelPreparer
    {
        public int Calls;
        public string? Error;
        public Task<string?> ClearModelAsync(CancellationToken ct) { Calls++; return Task.FromResult(Error); }
    }

    private readonly string _tempIfc;
    private readonly FakeRunner _runner = new();
    private readonly FakePreparer _preparer = new();
    private IngestHandler Handler => new(_runner, _preparer);

    public IngestHandlerTests()
    {
        _tempIfc = Path.Combine(Path.GetTempPath(), $"xylem_{Guid.NewGuid():N}.ifc");
        File.WriteAllText(_tempIfc, "ISO-10303-21;\nHEADER;\nENDSEC;\n");
    }

    [Fact]
    public async Task Merge_runs_the_runner_returns_counts_and_does_not_clear()
    {
        var result = await Handler.IngestAsync("Demo", IngestMode.Merge, _tempIfc, default);

        result.Success.Should().BeTrue();
        result.ThingsCreated.Should().Be(3);
        result.RelationshipsCreated.Should().Be(2);
        _runner.SeenName.Should().Be("Demo");
        _preparer.Calls.Should().Be(0, "merge does not clear the model");
    }

    [Fact]
    public async Task NewModel_clears_the_model_before_running()
    {
        await Handler.IngestAsync("Fresh", IngestMode.NewModel, _tempIfc, default);

        _preparer.Calls.Should().Be(1);
        _runner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task NewModel_clear_failure_aborts_before_running()
    {
        _preparer.Error = "broker refused the clear";

        var result = await Handler.IngestAsync("Fresh", IngestMode.NewModel, _tempIfc, default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("clear");
        _runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_name_fails_without_clearing_or_running()
    {
        var result = await Handler.IngestAsync("  ", IngestMode.NewModel, _tempIfc, default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("name");
        _preparer.Calls.Should().Be(0);
        _runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_file_fails_without_running()
    {
        var result = await Handler.IngestAsync("Demo", IngestMode.Merge, _tempIfc + ".nope", default);

        result.Success.Should().BeFalse();
        _runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Runner_failure_is_surfaced()
    {
        _runner.Result = new IngestRunResult(false, 0, 0, 0, "xbim parse error");

        var result = await Handler.IngestAsync("Demo", IngestMode.Merge, _tempIfc, default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("xbim parse error");
    }

    // ---- upload path (IngestUploadAsync) ----

    private static MemoryStream Ifc(int bytes = 32) => new(new byte[bytes]);

    [Fact]
    public async Task Upload_empty_stream_fails_without_running()
    {
        var result = await Handler.IngestUploadAsync(new MemoryStream(), "Demo", IngestMode.Merge, 1024, default);
        result.Success.Should().BeFalse();
        _runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Upload_over_the_size_cap_fails_without_running()
    {
        var result = await Handler.IngestUploadAsync(Ifc(10_000), "Demo", IngestMode.Merge, maxBytes: 1000, default);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("limit");
        _runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Upload_valid_spools_to_a_real_file_and_runs()
    {
        var result = await Handler.IngestUploadAsync(Ifc(64), "Demo", IngestMode.Merge, maxBytes: 1024, default);

        result.Success.Should().BeTrue();
        _runner.Calls.Should().Be(1);
        _runner.FileExistedAtCall.Should().BeTrue("the upload was spooled to a temp file before running");
        File.Exists(_runner.SeenPath!).Should().BeFalse("the temp upload file is cleaned up after the run");
    }

    public void Dispose()
    {
        if (File.Exists(_tempIfc)) File.Delete(_tempIfc);
    }
}
