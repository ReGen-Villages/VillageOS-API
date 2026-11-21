namespace vos.Service.Intake.Services;

public sealed class RequestBudgetRefresher(
    HeldDeclarations declarations, RequestBudgets budgets, TimeProvider clock, ILogger<RequestBudgetRefresher> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
