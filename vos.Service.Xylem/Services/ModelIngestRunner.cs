using System.Diagnostics;
using System.Text.RegularExpressions;
using vos.Service.Shared;

namespace vos.Service.Xylem.Services;

// Production runner: invokes the vos.Tools.ModelIngest tool as a subprocess
//   dotnet <ModelIngest.dll> --ifc <path> --post <mycelium> --name <model> --profile analysis
// which parses (Xbim), classifies, and posts the graph to /api/model/fragment (idempotent, stable ids).
// Purely the subprocess; new-model model preparation is the handler's job. No unit test starts the tool
// itself; the launch it builds, the guard, the reading of a child's output and the count parsing are
// tested, and the orchestration by IngestHandlerTests.
public sealed class ModelIngestRunner : IModelIngestRunner
{
    private const string CountLineForm = "Ingested: <n> things, <m> relationships.";

    // The ingest tool prints this line and no other count, so every Thing it wrote is reported as created
    // and none as updated.
    private static readonly Regex CountLine = new(@"Ingested:\s+(\d+)\s+things,\s+(\d+)\s+relationships",
        RegexOptions.Compiled);

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

        var run = await RunToExitAsync(await BuildStartInfoAsync(ifcPath, modelName, ct), ct);
        return run is null
            ? new IngestRunResult(false, 0, 0, 0, "Failed to start ModelIngest process.")
            : ResultOf(run);
    }

    internal IngestRunResult ResultOf(ProcessRun run)
    {
        if (run.ExitCode != 0)
        {
            _log.LogWarning("ModelIngest failed (exit {Code}): {Err}", run.ExitCode, run.StandardError);
            return new IngestRunResult(false, 0, 0, 0,
                string.IsNullOrWhiteSpace(run.StandardError) ? "IFC ingest failed." : run.StandardError.Trim());
        }

        if (ParseCounts(run.StandardOutput) is not var (things, relationships))
        {
            // The tool lives in another repository and can change what it prints without failing a test
            // here. The model was written, so the run stays a success, and the reply carries no counts.
            _log.LogWarning(
                "The ingest tool succeeded and printed no line of the form \"{CountLineForm}\", so the reply "
                + "carries no counts of what the tool wrote. It printed: {Output}",
                CountLineForm, run.StandardOutput.Trim());
            return new IngestRunResult(true, null, null, null, null);
        }

        return new IngestRunResult(true, things, 0, relationships, null);
    }

    internal sealed record ProcessRun(int ExitCode, string StandardOutput, string StandardError);

    // Both streams are read at once. A child that has filled the stream nobody is reading waits to write
    // more, and a reader that takes one stream to its end before starting the other waits for that child
    // to exit.
    internal static async Task<ProcessRun?> RunToExitAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var proc = Process.Start(startInfo);
        if (proc is null) return null;

        var standardOutput = proc.StandardOutput.ReadToEndAsync(ct);
        var standardError = proc.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(standardOutput, standardError);
        await proc.WaitForExitAsync(ct);

        return new ProcessRun(proc.ExitCode, await standardOutput, await standardError);
    }

    // The bearer token travels in the child's environment, never in its arguments: an argument list is
    // readable by anything that can list processes, and by any diagnostic that captures a command line.
    // An absent token is removed rather than left inherited, so the ingest tool authenticates with this
    // setting and nothing else.
    internal async Task<ProcessStartInfo> BuildStartInfoAsync(string ifcPath, string modelName, CancellationToken ct)
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

        var token = await _credential.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) psi.Environment.Remove("Token");
        else psi.Environment["Token"] = token;

        return psi;
    }

    internal static (int Things, int Relationships)? ParseCounts(string stdout)
    {
        var m = CountLine.Match(stdout);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }
}
