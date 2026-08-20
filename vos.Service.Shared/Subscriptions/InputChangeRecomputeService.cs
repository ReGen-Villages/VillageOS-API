using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

/// <summary>Builds the subscription client one model's follower speaks through. The token is asked for per
/// call rather than fixed at construction, because a follower replaces its own before it expires.</summary>
public delegate ISubscriptionClient SubscriptionClientFactory(Func<Task<string?>> currentToken);

public static class InputChangeRecomputeRegistration
{
    /// <summary>Wire the subscription that keeps a compute service's results current. The handler type is
    /// resolved per recompute rather than captured, so it follows whatever the container holds.</summary>
    public static IServiceCollection AddInputChangeRecompute<THandler>(
        this IServiceCollection services, string serviceName, string myceliumUrl, string? serviceToken,
        Func<THandler, IReadOnlySet<string>> inputProperties,
        Func<THandler, Guid, CancellationToken, Task> recompute)
        where THandler : notnull
    {
        services.AddSingleton<IServiceTokenExchange>(provider => new ServiceTokenExchange(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<ServiceTokenExchange>>(), myceliumUrl));

        services.AddSingleton<SubscriptionClientFactory>(provider => currentToken => new SubscriptionClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<SubscriptionClient>>(), myceliumUrl, tokenProvider: currentToken));

        services.AddSingleton(provider => new RecomputeInputs(serviceName,
            () => inputProperties(provider.GetRequiredService<THandler>()),
            (subjectId, cancellationToken) =>
                recompute(provider.GetRequiredService<THandler>(), subjectId, cancellationToken)));

        services.AddSingleton(provider => new InputChangeRecomputeService(
            provider.GetRequiredService<SubscriptionClientFactory>(),
            provider.GetRequiredService<IServiceTokenExchange>(),
            provider.GetRequiredService<RecomputeInputs>(),
            provider.GetRequiredService<IHostEnvironment>(),
            provider.GetRequiredService<ILogger<InputChangeRecomputeService>>(),
            serviceToken));

        return services.AddHostedService(provider => provider.GetRequiredService<InputChangeRecomputeService>());
    }
}

/// <summary>What a compute service needs to keep its results current: the inputs it reads off a
/// subject, and how to recompute one.</summary>
/// <param name="ServiceName">Names the service in log lines.</param>
/// <param name="InputProperties">Only these trigger a recompute. A service also writes its outputs onto
/// the subject it watches, so reacting to every change on that subject would recompute forever. Asked for
/// per change rather than fixed here, because a service whose inputs are named by the model learns them
/// when it first computes and a set captured at startup would answer for a model it has never read.</param>
public sealed record RecomputeInputs(
    string ServiceName,
    Func<IReadOnlySet<string>> InputProperties,
    Func<Guid, CancellationToken, Task> RecomputeAsync);

/// <summary>
/// Keeps a compute service's results current across every project it serves.
///
/// A subscription is bound to one model when Mycelium creates it, and a change event says nothing about
/// which model it came from. A daemon shared by several projects therefore cannot follow them all through
/// one subscription — it holds one per model instead, each opened with a token for that model.
///
/// The model is learned where it is already known: a service starts watching a subject inside the /handle
/// call that made it compute, and the bearer on that call names the caller's model. That bearer expires in
/// minutes, so it is exchanged for one that outlasts the subscription and replaced before it lapses.
///
/// Watching the subject alone covers both ways an input moves, because Mycelium publishes a derived value
/// on the Thing that owns it: a param someone edited arrives as a property change on the subject, and a
/// roll-up whose members changed arrives as a property change on the subject too.
///
/// A derived value is published live-only and never enters the journal, so a resumed stream does not
/// replay one. After a reconnect every watched subject is recomputed rather than trusted.
/// </summary>
public sealed class InputChangeRecomputeService : IHostedService
{
    private readonly SubscriptionClientFactory _subscriptionClientFor;
    private readonly IServiceTokenExchange _tokenExchange;
    private readonly RecomputeInputs _inputs;
    private readonly IHostEnvironment _environment;
    private readonly ILogger _logger;
    private readonly string? _startupToken;
    private readonly TimeSpan _replacementLeadTime;
    private readonly TimeSpan _replacementCheckInterval;

    private readonly Dictionary<Guid, ModelFollower> _followers = new();
    private readonly object _followersLock = new();

    private CancellationTokenSource? _cancellation;
    private bool _started;

    public InputChangeRecomputeService(
        SubscriptionClientFactory subscriptionClientFor, IServiceTokenExchange tokenExchange,
        RecomputeInputs inputs, IHostEnvironment environment, ILogger logger, string? startupToken = null,
        TimeSpan? replacementLeadTime = null, TimeSpan? replacementCheckInterval = null)
    {
        _subscriptionClientFor = subscriptionClientFor;
        _tokenExchange = tokenExchange;
        _inputs = inputs;
        _environment = environment;
        _logger = logger;
        _startupToken = startupToken;
        _replacementLeadTime = replacementLeadTime ?? TimeSpan.FromHours(2);
        _replacementCheckInterval = replacementCheckInterval ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>Start following a subject, in the model the work in hand belongs to. Called when the service
    /// computes for one, so the set grows from the dispatches the service already receives rather than from
    /// a discovery rule of its own.</summary>
    /// <summary>Follow a subject, and every Thing its result is computed from when those are not the
    /// subject itself. A service reading only what it writes passes none.</summary>
    public void Watch(Guid subjectId, params Guid[] readsFrom)
    {
        var bearer = ModelScopedBearer.Read(MyceliumModelToken.Current ?? _startupToken);
        if (bearer is null)
        {
            _logger.LogWarning(
                "{Service}: no model-scoped token is in hand, so {SubjectId} cannot be followed and its results will go stale",
                _inputs.ServiceName, subjectId);
            return;
        }

        ModelFollower follower;
        lock (_followersLock)
        {
            if (!_followers.TryGetValue(bearer.ModelId, out var existing))
            {
                existing = NewFollower(bearer);
                _followers[bearer.ModelId] = existing;
                if (_started) existing.Start(_cancellation!.Token);
            }
            follower = existing;
        }

        follower.Watch(subjectId, readsFrom);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Tests run against a synthetic Mycelium URL — don't open a real subscription.
        if (_environment.IsEnvironment("Testing")) return Task.CompletedTask;

        _cancellation = new CancellationTokenSource();

        lock (_followersLock)
        {
            _started = true;
            foreach (var follower in _followers.Values)
                follower.Start(_cancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellation?.Cancel();

        List<ModelFollower> followers;
        lock (_followersLock)
        {
            _started = false;
            followers = _followers.Values.ToList();
        }

        foreach (var follower in followers)
            await follower.StopAsync(cancellationToken);
    }

    private ModelFollower NewFollower(ModelScopedBearer seed) => new(
        seed, _subscriptionClientFor, _tokenExchange, _inputs, _logger,
        _replacementLeadTime, _replacementCheckInterval);
}
