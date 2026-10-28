using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Shared;

// A /handle call's token expires minutes after Mycelium issued it, so work that outlives the call swaps it
// for one lasting a day while the call is still being answered, and swaps again before that one runs out.
// Swapping when the work starts would be too late for work queued behind other work.
public sealed class HeldModelToken
{
    private static readonly TimeSpan ReplacementLeadTime = TimeSpan.FromHours(2);

    // For a call that carried no token, as when a service runs without authentication: the work presents
    // the service's own credential.
    public static HeldModelToken None { get; } = new(null, null, NullLogger.Instance);

    private readonly IServiceTokenExchange? _exchange;
    private readonly ILogger _logger;
    private ModelScopedBearer? _bearer;

    private HeldModelToken(IServiceTokenExchange? exchange, ModelScopedBearer? bearer, ILogger logger)
    {
        _exchange = exchange;
        _bearer = bearer;
        _logger = logger;
    }

    // Null when Mycelium would not swap the call's token: work started on it would be refused partway through.
    public static async Task<HeldModelToken?> SwapFromTheCallAsync(
        IServiceTokenExchange exchange, ILogger logger, CancellationToken cancellationToken = default)
    {
        var callToken = MyceliumModelToken.Current;
        if (string.IsNullOrEmpty(callToken))
            return None;

        var swapped = await exchange.ExchangeAsync(callToken, cancellationToken);
        return swapped is null ? null : new HeldModelToken(exchange, swapped, logger);
    }

    public async Task ActAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        if (_exchange is null)
        {
            await work();
            return;
        }

        await ReplaceIfDueAsync(cancellationToken);
        await MyceliumModelToken.ActingForAsync(Volatile.Read(ref _bearer)!.Token, work);
    }

    private async Task ReplaceIfDueAsync(CancellationToken cancellationToken)
    {
        var held = Volatile.Read(ref _bearer)!;
        if (!held.IsDueForReplacement(DateTimeOffset.UtcNow, ReplacementLeadTime))
            return;

        var replacement = await _exchange!.ExchangeAsync(held.Token, cancellationToken);
        if (replacement is null)
        {
            _logger.LogWarning(
                "Could not replace the token held for model {ModelId}; work under it is refused from {ExpiresAt}",
                held.ModelId, held.ExpiresAt);
            return;
        }

        Volatile.Write(ref _bearer, replacement);
    }
}
