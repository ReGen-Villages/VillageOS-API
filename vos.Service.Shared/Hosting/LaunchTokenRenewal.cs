using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Shared.Hosting;

public static class LaunchTokenRegistration
{
    public static LaunchToken? AddLaunchToken(this IServiceCollection services, string? token, string myceliumUrl)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var launchToken = new LaunchToken(token);
        services.AddServiceTokenExchange(myceliumUrl);
        services.AddHostedService(provider => new LaunchTokenRenewal(
            launchToken,
            provider.GetRequiredService<IServiceTokenExchange>(),
            provider.GetRequiredService<ILogger<LaunchTokenRenewal>>(),
            ModelScopedBearer.ReplacementCheckInterval));
        return launchToken;
    }
}

internal sealed class LaunchTokenRenewal : BackgroundService
{
    private readonly LaunchToken _launchToken;
    private readonly IServiceTokenExchange _exchange;
    private readonly ILogger _logger;
    private readonly TimeSpan _checkInterval;

    public LaunchTokenRenewal(
        LaunchToken launchToken, IServiceTokenExchange exchange, ILogger logger, TimeSpan checkInterval)
    {
        _launchToken = launchToken;
        _exchange = exchange;
        _logger = logger;
        _checkInterval = checkInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var renewable = true;
        while (renewable)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            renewable = await ReplaceIfDueAsync(DateTimeOffset.UtcNow, stoppingToken);
        }
    }

    // False when the token can never be swapped: it has expired, or it names no model or no expiry.
    internal async Task<bool> ReplaceIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (ModelScopedBearer.Read(_launchToken.Current) is not { } held)
            return false;

        if (held.ExpiresAt <= now)
        {
            _logger.LogError(
                "The token this service was launched with expired at {ExpiresAt} before it could be replaced; "
                + "the broker refuses this service's own calls until it is restarted", held.ExpiresAt);
            return false;
        }

        if (!held.IsDueForReplacement(now, ModelScopedBearer.ReplacementLeadTime))
            return true;

        var replacement = await _exchange.ExchangeAsync(held.Token, cancellationToken);
        if (replacement is null)
        {
            _logger.LogWarning(
                "Could not replace the token this service was launched with; it tries again in {Interval}, "
                + "and the token expires at {ExpiresAt}", _checkInterval, held.ExpiresAt);
            return true;
        }

        _launchToken.Replace(replacement.Token);
        _logger.LogInformation(
            "Replaced the token this service was launched with; the new one expires at {ExpiresAt}",
            replacement.ExpiresAt);
        return true;
    }
}
