namespace vos.Taproot;

/// <summary>
/// What the snapshot read path has cost against the writers, from the CLI. Totals run from process
/// start, so `snapshots` prints them raw and says how to turn two readings into a rate for one run.
/// </summary>
public class SnapshotsCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public SnapshotsCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        if (!string.IsNullOrWhiteSpace(_arg))
        {
            ShowUsage();
            return;
        }

        try
        {
            await ShowResolutionMetricsAsync();
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + ex.Message);
        }
    }

    private async Task ShowResolutionMetricsAsync()
    {
        var metrics = await _mycelium.GetSnapshotResolutionMetricsAsync();
        var resolutions = metrics.GetProperty("Resolutions").GetInt64();
        var retries = metrics.GetProperty("Retries").GetInt64();
        var lockedPasses = metrics.GetProperty("LockedPasses").GetInt64();

        _writer.WriteLine("Snapshot resolution — since Mycelium started");
        _writer.WriteLine($"  Resolutions served    {resolutions,10}");
        _writer.WriteLine($"  Taken again           {retries,10}{Share(retries, resolutions)}");
        _writer.WriteLine($"  Took the lock         {lockedPasses,10}{Share(lockedPasses, resolutions)}");
        _writer.WriteLine();
        _writer.WriteLine("Totals include the seed load's structural writes. For one run's rate,");
        _writer.WriteLine("read before and after it and difference the two.");
    }

    private static string Share(long part, long total) =>
        total == 0 ? "" : $"   {(double)part / total:P1} of resolutions";

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  snapshots          - Resolutions served, taken again, and forced to take the lock");
    }
}
