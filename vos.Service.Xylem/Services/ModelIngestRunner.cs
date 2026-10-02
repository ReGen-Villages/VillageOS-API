using System.Diagnostics;
using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.Xylem.Services;

// Production runner: invokes the vos.Tools.ModelIngest tool as a subprocess
//   dotnet <ModelIngest.dll> --ifc <path> --post <mycelium> --name <model> --profile analysis --result <path>
// which parses (Xbim), classifies, and posts the graph to /api/model/fragment (idempotent, stable ids).
// Purely the subprocess; new-model model preparation is the handler's job. No unit test starts the tool
// itself; the launch it builds, the guard, the reading of a child's output and of its result file and
// the stopping of a child are tested, and the orchestration by IngestHandlerTests.
public sealed class ModelIngestRunner : IModelIngestRunner
{
    // What the ingest tool writes to its --result file: what the broker created and updated across the
    // post, under the names the broker answers a fragment with.
    private sealed record BrokerTotals(int ThingsCreated, int ThingsUpdated, int RelationshipsCreated);

    private static readonly JsonSerializerOptions ResultFileNames = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };

    private readonly string _modelIngestDll;
    private readonly string _myceliumUrl;
    private readonly ServiceCredential _credential;
    private readonly ILogger<ModelIngestRunner> _log;

    public ModelIngestRunner(
        string modelIngestDll, string myceliumUrl, ServiceCredential credential, ILogger<ModelIngestRunner> log)
    {
        _modelIngestDll = modelIngestDll;
        _myceliumUrl = myceliumUrl;
        _credential = credential;
        _log = log;
    }

    public async Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_modelIngestDll) || !File.Exists(_modelIngestDll))
            return new IngestRunResult(false, 0, 0, 0, $"ModelIngest tool not found at '{_modelIngestDll}'.");

        var resultPath = Path.Combine(Path.GetTempPath(), $"xylem_{Guid.NewGuid():N}.result.json");
        return await RunAndReadResultAsync(
            await BuildStartInfoAsync(ifcPath, modelName, resultPath, ct), resultPath, ct);
    }

    internal async Task<IngestRunResult> RunAndReadResultAsync(
        ProcessStartInfo startInfo, string resultPath, CancellationToken ct)
    {
        try
        {
            var run = await RunToExitAsync(startInfo, ct);
            if (run is null) return new IngestRunResult(false, 0, 0, 0, "Failed to start ModelIngest process.");

            return ResultOf(run, File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath, ct) : null);
        }
        finally
        {
            File.Delete(resultPath);
        }
    }

    internal IngestRunResult ResultOf(ProcessRun run, string? resultFile)
    {
        if (run.ExitCode != 0)
        {
            _log.LogWarning("ModelIngest failed (exit {Code}): {Err}", run.ExitCode, run.StandardError);
            return new IngestRunResult(false, 0, 0, 0,
                string.IsNullOrWhiteSpace(run.StandardError) ? "IFC ingest failed." : run.StandardError.Trim());
        }

        if (TotalsIn(resultFile) is not { } totals)
        {
            // The tool writes no file when a reply from the broker carried no counts, and says so on its
            // error output. The model was written, so the run stays a success, and the reply carries no
            // counts.
            _log.LogWarning(
                "The ingest tool succeeded and left no result file the service could read, so the reply "
                + "carries no counts of what the broker created and updated. Result file: {ResultFile}. "
                + "The tool's error output: {Err}. It printed: {Output}",
                resultFile ?? "none was written", run.StandardError.Trim(), run.StandardOutput.Trim());
            return new IngestRunResult(true, null, null, null, null);
        }

        return new IngestRunResult(
            true, totals.ThingsCreated, totals.ThingsUpdated, totals.RelationshipsCreated, null);
    }

    private static BrokerTotals? TotalsIn(string? resultFile)
    {
        if (resultFile is null) return null;

        try
        {
            return JsonSerializer.Deserialize<BrokerTotals>(resultFile, ResultFileNames);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record ProcessRun(int ExitCode, string StandardOutput, string StandardError);

    // Both streams are read at once. A child that has filled the stream nobody is reading waits to write
    // more, and a reader that takes one stream to its end before starting the other waits for that child
    // to exit.
    internal async Task<ProcessRun?> RunToExitAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var proc = Process.Start(startInfo);
        if (proc is null) return null;

        try
        {
            var standardOutput = proc.StandardOutput.ReadToEndAsync(ct);
            var standardError = proc.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(standardOutput, standardError);
            await proc.WaitForExitAsync(ct);

            return new ProcessRun(proc.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            // Disposing the process object leaves the process running. The callers delete the tool's input
            // file and its result file as soon as this method ends, so the tool has to be gone before it does.
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(CancellationToken.None);
            _log.LogWarning(
                "The ingest run was cancelled, so the tool was stopped before it finished. "
                + "The model keeps what the tool had already posted.");
            throw;
        }
    }

    // The bearer token travels in the child's environment, never in its arguments: an argument list is
    // readable by anything that can list processes, and by any diagnostic that captures a command line.
    // An absent token is removed rather than left inherited, so the ingest tool authenticates with this
    // setting and nothing else.
    internal async Task<ProcessStartInfo> BuildStartInfoAsync(
        string ifcPath, string modelName, string resultPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(_modelIngestDll);
        psi.ArgumentList.Add("--ifc"); psi.ArgumentList.Add(ifcPath);
        psi.ArgumentList.Add("--post"); psi.ArgumentList.Add(_myceliumUrl);
        psi.ArgumentList.Add("--name"); psi.ArgumentList.Add(modelName);
        psi.ArgumentList.Add("--profile"); psi.ArgumentList.Add("analysis");
        psi.ArgumentList.Add("--result"); psi.ArgumentList.Add(resultPath);

        var token = await _credential.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) psi.Environment.Remove("Token");
        else psi.Environment["Token"] = token;

        return psi;
    }
}
