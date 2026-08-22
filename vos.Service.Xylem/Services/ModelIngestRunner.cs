using System.Diagnostics;
using System.Text.RegularExpressions;

namespace vos.Service.Xylem.Services;

// Production runner: invokes the vos.Tools.ModelIngest tool as a subprocess
//   dotnet <ModelIngest.dll> --ifc <path> --post <mycelium> --name <model> --profile analysis
// which parses (Xbim), classifies, and posts the graph to /api/model/fragment (idempotent, stable ids).
// Purely the subprocess; new-model model preparation is the handler's job. The spawn itself is not unit-
// tested; the launch it builds, the guard and the count parsing are, and the orchestration by
// IngestHandlerTests.
public sealed class ModelIngestRunner : IModelIngestRunner
{
    // The ModelIngest CLI prints "Ingested <n> things, <m> relationships." — the only count it surfaces.
    // Created-vs-updated fidelity needs a ModelIngest enhancement (follow-up); we report totals as created.
    private static readonly Regex CountLine = new(@"Ingested\s+(\d+)\s+things,\s+(\d+)\s+relationships",
        RegexOptions.Compiled);

    private readonly string _modelIngestDll;
    private readonly string _myceliumUrl;
    private readonly string? _token;
    private readonly ILogger<ModelIngestRunner> _log;

    public ModelIngestRunner(string modelIngestDll, string myceliumUrl, string? token, ILogger<ModelIngestRunner> log)
    {
        _modelIngestDll = modelIngestDll;
        _myceliumUrl = myceliumUrl;
        _token = token;
        _log = log;
    }

    public async Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_modelIngestDll) || !File.Exists(_modelIngestDll))
            return new IngestRunResult(false, 0, 0, 0, $"ModelIngest tool not found at '{_modelIngestDll}'.");

        using var proc = Process.Start(BuildStartInfo(ifcPath, modelName));
        if (proc is null) return new IngestRunResult(false, 0, 0, 0, "Failed to start ModelIngest process.");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            _log.LogWarning("ModelIngest failed (exit {Code}): {Err}", proc.ExitCode, stderr);
            return new IngestRunResult(false, 0, 0, 0, string.IsNullOrWhiteSpace(stderr) ? "IFC ingest failed." : stderr.Trim());
        }

        var (things, rels) = ParseCounts(stdout);
        return new IngestRunResult(true, things, 0, rels, null);
    }

    // The bearer token travels in the child's environment, never in its arguments: an argument list is
    // readable by anything that can list processes, and by any diagnostic that captures a command line.
    // An absent token is removed rather than left inherited, so the ingest tool authenticates with this
    // setting and nothing else.
    internal ProcessStartInfo BuildStartInfo(string ifcPath, string modelName)
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

        if (string.IsNullOrEmpty(_token)) psi.Environment.Remove("Token");
        else psi.Environment["Token"] = _token;

        return psi;
    }

    // Pull "Ingested <n> things, <m> relationships" out of the CLI output; (0,0) if absent.
    internal static (int Things, int Relationships) ParseCounts(string stdout)
    {
        var m = CountLine.Match(stdout);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }
}
