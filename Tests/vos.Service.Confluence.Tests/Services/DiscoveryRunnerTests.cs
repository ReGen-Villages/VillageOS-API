using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Confluence.Helpers;
using vos.Service.Confluence.Services;
using Xunit;

namespace vos.Service.Confluence.Tests.Services;

public class DiscoveryRunnerTests
{
    private static CoveringSource Source(string name) => new(Guid.NewGuid(), name, name + "Endpoint");

    private static IReadOnlyList<CoveringSource> Sources(params string[] names) =>
        names.Select(Source).ToList();

    private static readonly Dictionary<string, string> NoParameters = new();

    // A fetcher a test drives by name, so a scenario reads as "this one fails, that one is slow".
    private sealed class ScriptedFetcher : ISourceFetcher
    {
        private readonly Func<string, CancellationToken, Task<SourceOutcome>> _respond;
        private int _inFlight;

        public ScriptedFetcher(Func<string, CancellationToken, Task<SourceOutcome>> respond) => _respond = respond;

        public int PeakInFlight { get; private set; }
        public List<string> Called { get; } = new();

        public async Task<SourceOutcome> FetchAsync(
            string sourceName, string endpointName,
            IReadOnlyDictionary<string, string> addressParameters, CancellationToken cancellationToken)
        {
            lock (Called) Called.Add(sourceName);
            var now = Interlocked.Increment(ref _inFlight);
            lock (Called) PeakInFlight = Math.Max(PeakInFlight, now);
            try
            {
                return await _respond(sourceName, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static DiscoveryRunner Runner(ISourceFetcher fetcher, int maxConcurrent = 4) =>
        new(fetcher, maxConcurrent, NullLogger<DiscoveryRunner>.Instance);

    private static Task<SourceOutcome> Resolved(string name) =>
        Task.FromResult(new SourceOutcome(name, true, null));

    [Fact]
    public async Task RunAsync_CallsEverySourceCoveringTheSite()
    {
        var fetcher = new ScriptedFetcher((name, _) => Resolved(name));

        var report = await Runner(fetcher).RunAsync(
            Guid.NewGuid(), Sources("OpenMeteo", "FloodPortal", "Copernicus"), NoParameters, default);

        fetcher.Called.Should().BeEquivalentTo("OpenMeteo", "FloodPortal", "Copernicus");
        report.Resolved.Select(outcome => outcome.Source)
            .Should().BeEquivalentTo("OpenMeteo", "FloodPortal", "Copernicus");
        report.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_OneSourceFailing_LeavesTheOthersResolved()
    {
        // The requirement that decides whether this is usable at all: public data portals go down,
        // and an intake that aborted because one provider was unavailable would be abandoned.
        var fetcher = new ScriptedFetcher((name, _) => name == "FloodPortal"
            ? Task.FromResult(new SourceOutcome(name, false, "503 from the provider"))
            : Resolved(name));

        var report = await Runner(fetcher).RunAsync(
            Guid.NewGuid(), Sources("OpenMeteo", "FloodPortal", "Copernicus"), NoParameters, default);

        report.Resolved.Select(outcome => outcome.Source).Should().BeEquivalentTo("OpenMeteo", "Copernicus");
        report.Unresolved.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SourceOutcome("FloodPortal", false, "503 from the provider"));
    }

    [Fact]
    public async Task RunAsync_SourceThrowing_IsRecordedUnresolvedRatherThanEndingTheRun()
    {
        // The fetcher reports a failed source rather than throwing, but it reaches the network. An
        // exception raised on one source's behalf must not be handed to the caller of the run.
        var fetcher = new ScriptedFetcher((name, _) => name == "Broken"
            ? throw new HttpRequestException("connection reset")
            : Resolved(name));

        var report = await Runner(fetcher).RunAsync(
            Guid.NewGuid(), Sources("OpenMeteo", "Broken"), NoParameters, default);

        report.Resolved.Select(outcome => outcome.Source).Should().Equal("OpenMeteo");
        report.Unresolved.Should().ContainSingle();
        report.Unresolved[0].Source.Should().Be("Broken");
        report.Unresolved[0].Reason.Should().Contain("connection reset");
    }

    [Fact]
    public async Task RunAsync_EveryUnresolvedSource_CarriesAReason()
    {
        var fetcher = new ScriptedFetcher((name, _) => name switch
        {
            "TimedOut" => throw new TaskCanceledException("timed out"),
            "Refused" => Task.FromResult(new SourceOutcome(name, false, "404 from the provider")),
            _ => Resolved(name),
        });

        var report = await Runner(fetcher).RunAsync(
            Guid.NewGuid(), Sources("TimedOut", "Refused", "Fine"), NoParameters, default);

        report.Unresolved.Should().HaveCount(2);
        report.Unresolved.Should().OnlyContain(outcome => !string.IsNullOrWhiteSpace(outcome.Reason));
    }

    [Fact]
    public async Task RunAsync_HoldsSourcesInFlightToTheConfiguredBound()
    {
        // A site covered by many sources must not open a burst of connections that reads as abuse.
        var release = new TaskCompletionSource();
        var fetcher = new ScriptedFetcher(async (name, _) =>
        {
            await release.Task;
            return new SourceOutcome(name, true, null);
        });
        var sources = Sources("a", "b", "c", "d", "e", "f", "g", "h");

        var run = Runner(fetcher, maxConcurrent: 3)
            .RunAsync(Guid.NewGuid(), sources, NoParameters, default);
        while (fetcher.Called.Count < 3) await Task.Delay(5);
        await Task.Delay(50);
        release.SetResult();
        var report = await run;

        fetcher.PeakInFlight.Should().BeLessThanOrEqualTo(3);
        report.Resolved.Should().HaveCount(8);
    }

    [Fact]
    public async Task RunAsync_NoCoveringSources_ReportsBothHalvesEmpty()
    {
        var fetcher = new ScriptedFetcher((name, _) => Resolved(name));

        var report = await Runner(fetcher).RunAsync(Guid.NewGuid(), Sources(), NoParameters, default);

        report.Resolved.Should().BeEmpty();
        report.Unresolved.Should().BeEmpty();
        fetcher.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesTheSitesValuesToEverySource()
    {
        var seen = new List<IReadOnlyDictionary<string, string>>();
        var fetcher = new CapturingFetcher(seen);
        var parameters = new Dictionary<string, string> { ["lat"] = "-25.75", ["lng"] = "28.19" };

        await Runner(fetcher).RunAsync(Guid.NewGuid(), Sources("OpenMeteo", "FloodPortal"), parameters, default);

        seen.Should().HaveCount(2);
        seen.Should().OnlyContain(values => values["lat"] == "-25.75" && values["lng"] == "28.19");
    }

    private sealed class CapturingFetcher : ISourceFetcher
    {
        private readonly List<IReadOnlyDictionary<string, string>> _seen;
        public CapturingFetcher(List<IReadOnlyDictionary<string, string>> seen) => _seen = seen;

        public Task<SourceOutcome> FetchAsync(
            string sourceName, string endpointName,
            IReadOnlyDictionary<string, string> addressParameters, CancellationToken cancellationToken)
        {
            lock (_seen) _seen.Add(addressParameters);
            return Task.FromResult(new SourceOutcome(sourceName, true, null));
        }
    }
}
