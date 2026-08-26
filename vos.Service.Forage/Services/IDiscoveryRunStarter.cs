namespace vos.Service.Forage.Services;

// Starts a run off the request that dispatched it. The platform gives a dispatch fifteen seconds, and a
// run over a catalogue of tens of sources fetched a few at a time outlasts that — so answering first is
// what keeps finished work from being recorded as failed and driven a second time. What ends the dispatch
// is the model: the site shows a source has written onto it.
//
// An interface so a test can run the work where it can be awaited. Nothing else about it varies.
public interface IDiscoveryRunStarter
{
    void Start(Func<CancellationToken, Task> run);
}

// The run outlives the request, so it cannot take the request's cancellation token — that one is
// cancelled as the response completes, which would abort every run at the moment it started. It takes
// the host's instead, so a shutdown stops a run in flight and nothing else does.
public sealed class DiscoveryRunStarter : IDiscoveryRunStarter
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DiscoveryRunStarter> _logger;

    public DiscoveryRunStarter(IHostApplicationLifetime lifetime, ILogger<DiscoveryRunStarter> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public void Start(Func<CancellationToken, Task> run) => _ = Task.Run(async () =>
    {
        // Nothing awaits this task, so an exception escaping here would be unobserved: reported by
        // nothing, and on some configurations taking the process with it.
        try
        {
            await run(_lifetime.ApplicationStopping);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Discovery run failed after its dispatch had been accepted");
        }
    });
}
