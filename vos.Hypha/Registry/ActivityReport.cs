using Microsoft.Extensions.Logging;
using vos.Taproot;

namespace vos.Hypha.Registry;

public sealed class ActivityReport(MyceliumClient mycelium, ILogger<ActivityReport> logger)
{
    private const string Action = "action";

    // A report Mycelium does not take never fails the write it describes, which has already happened.
    public async Task<bool> ReportAsync(string description, bool succeeded = true)
    {
        try
        {
            await mycelium.ReportActivityAsync(Action, description, succeeded);
            return true;
        }
        catch (Exception refused) when (refused is HttpRequestException or KeyRefusedException or TaskCanceledException)
        {
            logger.LogWarning("Mycelium did not take the report of a change: {Reason}", OperatorMessage.For(refused));
            return false;
        }
    }
}
