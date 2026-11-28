using vos.Service.Phloem.Execution;

namespace vos.Service.Phloem.Tests;

// The start-up sequence lists once and hands the listing to each recovery; a case about one recovery lists just before it.
internal static class RecoveringFromAListing
{
    public static async Task<int> SendOwedReportsAsync(this PipelineExecutor executor) =>
        executor.SendOwedReports(await executor.ListHeldRunsAsync(CancellationToken.None));

    public static async Task<RecoveredWaits> RecoverWaitsAsync(this PipelineExecutor executor) =>
        await executor.RecoverWaitsAsync(await executor.ListHeldRunsAsync(CancellationToken.None), CancellationToken.None);

    public static async Task<int> RecoverUnfinishedAsync(this PipelineExecutor executor) =>
        await executor.RecoverUnfinishedAsync(await executor.ListHeldRunsAsync(CancellationToken.None), CancellationToken.None);

    public static async Task<int> RecoverQueuedAsync(this PipelineExecutor executor) =>
        await executor.RecoverQueuedAsync(await executor.ListHeldRunsAsync(CancellationToken.None));
}
