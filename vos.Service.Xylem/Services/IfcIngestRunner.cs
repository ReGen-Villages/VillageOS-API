using System.Diagnostics;
using System.Text.RegularExpressions;

namespace vos.Service.Xylem.Services;

// Production runner: invokes the vos.Tools.IfcIngest tool as a subprocess
//   dotnet <IfcIngest.dll> --ifc <path> --post <mycelium> --token <jwt> --name <model> --profile analysis
// which parses (Xbim), classifies, and posts the graph to /api/model/fragment (idempotent, stable ids).
// Purely the subprocess; new-model model preparation is the handler's job. Not unit-tested (it spawns a
// process); the guard + count parsing are covered, and the orchestration by IngestHandlerTests.
public sealed class IfcIngestRunner : IIfcIngestRunner
{
    // The IfcIngest CLI prints "Ingested <n> things, <m> relationships." — the only count it surfaces.
    // Created-vs-updated fidelity needs an IfcIngest enhancement (follow-up); we report totals as created.
    private static readonly Regex CountLine = new(@"Ingested\s+(\d+)\s+things,\s+(\d+)\s+relationships",
        RegexOptions.Compiled);

    private readonly string _ifcIngestDll;
    private readonly string _myceliumUrl;
    private readonly string? _token;
    private readonly ILogger<IfcIngestRunner> _log;

    public IfcIngestRunner(string ifcIngestDll, string myceliumUrl, string? token, ILogger<IfcIngestRunner> log)
    {
        _ifcIngestDll = ifcIngestDll;
        _myceliumUrl = myceliumUrl;
        _token = token;
        _log = log;
    }

    public async Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_ifcIngestDll) || !File.Exists(_ifcIngestDll))
            return new IngestRunResult(false, 0, 0, 0, $"IfcIngest tool not found at '{_ifcIngestDll}'.");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(_ifcIngestDll);
        psi.ArgumentList.Add("--ifc"); psi.ArgumentList.Add(ifcPath);
        psi.ArgumentList.Add("--post"); psi.ArgumentList.Add(_myceliumUrl);
        if (!string.IsNullOrEmpty(_token)) { psi.ArgumentList.Add("--token"); psi.ArgumentList.Add(_token); }
        psi.ArgumentList.Add("--name"); psi.ArgumentList.Add(modelName);
        psi.ArgumentList.Add("--profile"); psi.ArgumentList.Add("analysis");

        using var proc = Process.Start(psi);
        if (proc is null) return new IngestRunResult(false, 0, 0, 0, "Failed to start IfcIngest process.");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            _log.LogWarning("IfcIngest failed (exit {Code}): {Err}", proc.ExitCode, stderr);
            return new IngestRunResult(false, 0, 0, 0, string.IsNullOrWhiteSpace(stderr) ? "IFC ingest failed." : stderr.Trim());
        }

        var (things, rels) = ParseCounts(stdout);
        return new IngestRunResult(true, things, 0, rels, null);
    }

    // Pull "Ingested <n> things, <m> relationships" out of the CLI output; (0,0) if absent.
    internal static (int Things, int Relationships) ParseCounts(string stdout)
    {
        var m = CountLine.Match(stdout);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }
}
