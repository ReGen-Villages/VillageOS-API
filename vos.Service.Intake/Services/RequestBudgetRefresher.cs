namespace vos.Service.Intake.Services;

// Reads the budgets off the request path, because the rate limiter chooses a partition synchronously and cannot wait on the model.
public sealed class RequestBudgetRefresher(
    HeldDeclarations declarations, RequestBudgets budgets, TimeProvider clock, ILogger<RequestBudgetRefresher> logger)
    : BackgroundService
{
    private readonly HashSet<string> _reasonsLogged = [];
    private bool _readFailing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            try { await Task.Delay(HeldDeclarations.HeldFor, clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellation)
    {
        DeclaredRequestBudgets read;
        try
        {
            read = await declarations.ReadAsync(RequestBudgetReader.Selector(), RequestBudgetReader.Read, cancellation);
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested)
        {
            if (!_readFailing)
                logger.LogWarning("The request budgets could not be read, so the last ones read still apply: {Reason}", error.Message);
            _readFailing = true;
            return;
        }

        _readFailing = false;
        budgets.Replace(read);
        foreach (var reason in read.FallbackReasons)
            if (_reasonsLogged.Add(reason))
                logger.LogWarning("A request budget in the model cannot be held to: {Reason}", reason);
        _reasonsLogged.IntersectWith(read.FallbackReasons);
    }
}
