using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class DiscoveryRunnerTests
{
    private static readonly Dictionary<string, string> NoParameters = new();

    private static SourceCall CallAbout(Guid subject, string name = "",
        IReadOnlyDictionary<string, string>? values = null) =>
        new(subject, name, values ?? NoParameters);

    private static CoveringSource Source(string name, params SourceCall[] calls) =>
        new(Guid.NewGuid(), name, name + "Endpoint", calls);

    private static IReadOnlyList<CoveringSource> SourcesAbout(Guid site, params string[] names) =>
        names.Select(name => Source(name, CallAbout(site))).ToList();

    // A fetcher a test drives by name, so a scenario reads as "this one fails, that one is slow".
    private sealed class ScriptedFetcher : ISourceFetcher
    {
        private readonly Func<string, Task<SourceOutcome>> _respond;
        private int _inFlight;

        public ScriptedFetcher(Func<string, Task<SourceOutcome>> respond) => _respond = respond;

        public int PeakInFlight { get; private set; }
        public List<string> Called { get; } = new();

        public List<Guid> Subjects { get; } = new();

        public async Task<SourceOutcome> FetchAsync(
            Guid subjectId, string sourceName, string endpointName,
            IReadOnlyDictionary<string, string> addressParameters, CancellationToken cancellationToken)
        {
            lock (Called) { Called.Add(sourceName); Subjects.Add(subjectId); }
            var now = Interlocked.Increment(ref _inFlight);
            lock (Called) PeakInFlight = Math.Max(PeakInFlight, now);
            try
            {
                return await _respond(sourceName);
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
        var fetcher = new ScriptedFetcher(name => Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(
            site, SourcesAbout(site, "OpenMeteo", "FloodPortal", "Copernicus"), default);

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
        var fetcher = new ScriptedFetcher(name => name == "FloodPortal"
            ? Task.FromResult(new SourceOutcome(name, false, "503 from the provider"))
            : Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(
            site, SourcesAbout(site, "OpenMeteo", "FloodPortal", "Copernicus"), default);

        report.Resolved.Select(outcome => outcome.Source).Should().BeEquivalentTo("OpenMeteo", "Copernicus");
        report.Unresolved.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new SourceOutcome("FloodPortal", false, "503 from the provider"),
                // The identifiers a run records the answer against are asserted where they matter, on
                // the ledger; here the outcome's own words are what is being held.
                options => options.Excluding(outcome => outcome.SubjectId).Excluding(outcome => outcome.SourceId));
    }

    [Fact]
    public async Task RunAsync_SourceThrowing_IsRecordedUnresolvedRatherThanEndingTheRun()
    {
        // The fetcher reports a failed source rather than throwing, but it reaches the network. An
        // exception raised on one source's behalf must not be handed to the caller of the run.
        var fetcher = new ScriptedFetcher(name => name == "Broken"
            ? throw new HttpRequestException("connection reset")
            : Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(site, SourcesAbout(site, "OpenMeteo", "Broken"), default);

        report.Resolved.Select(outcome => outcome.Source).Should().Equal("OpenMeteo");
        report.Unresolved.Should().ContainSingle();
        report.Unresolved[0].Source.Should().Be("Broken");
        report.Unresolved[0].Reason.Should().Contain("connection reset");
    }

    [Fact]
    public async Task RunAsync_EveryUnresolvedSource_CarriesAReason()
    {
        var fetcher = new ScriptedFetcher(name => name switch
        {
            "TimedOut" => throw new TaskCanceledException("timed out"),
            "Refused" => Task.FromResult(new SourceOutcome(name, false, "404 from the provider")),
            _ => Resolved(name),
        });
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(
            site, SourcesAbout(site, "TimedOut", "Refused", "Fine"), default);

        report.Unresolved.Should().HaveCount(2);
        report.Unresolved.Should().OnlyContain(outcome => !string.IsNullOrWhiteSpace(outcome.Reason));
    }

    [Fact]
    public async Task RunAsync_HoldsCallsInFlightToTheConfiguredBound()
    {
        // A site covered by many sources must not open a burst of connections that reads as abuse —
        // and a source called once per assessment must not widen the burst either, so the bound is
        // held across every call rather than per source.
        var release = new TaskCompletionSource();
        var fetcher = new ScriptedFetcher(async name =>
        {
            await release.Task;
            return new SourceOutcome(name, true, null);
        });
        var site = Guid.NewGuid();
        var sources = SourcesAbout(site, "a", "b", "c", "d", "e", "f")
            .Append(Source("perAssessment", CallAbout(Guid.NewGuid()), CallAbout(Guid.NewGuid())))
            .ToList();

        var run = Runner(fetcher, maxConcurrent: 3).RunAsync(site, sources, default);
        // Bounded, and generously: a wait with no bound hangs the suite rather than failing it when
        // the runner admits too few, and CI agents are far slower than a development machine.
        var waited = TimeSpan.Zero;
        while (fetcher.Called.Count < 3 && waited < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(5);
            waited += TimeSpan.FromMilliseconds(5);
        }
        fetcher.Called.Count.Should().BeGreaterThanOrEqualTo(3, "three may run at once");
        await Task.Delay(50);
        release.SetResult();
        var report = await run;

        fetcher.PeakInFlight.Should().BeLessThanOrEqualTo(3);
        report.Resolved.Should().HaveCount(8);
    }

    [Fact]
    public async Task RunAsync_NoCoveringSources_ReportsBothHalvesEmpty()
    {
        var fetcher = new ScriptedFetcher(name => Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(site, SourcesAbout(site), default);

        report.Resolved.Should().BeEmpty();
        report.Unresolved.Should().BeEmpty();
        fetcher.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SourceWithNoCalls_IsNotFetchedAndNotReported()
    {
        // It declared what it resolves onto and the site holds nothing of it. Nothing to fetch is
        // not a failure, and an outcome either way would say more than the run knows.
        var fetcher = new ScriptedFetcher(name => Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(site, new[] { Source("HazardPortal") }, default);

        report.Resolved.Should().BeEmpty();
        report.Unresolved.Should().BeEmpty();
        fetcher.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesEachCallsOwnValuesToTheFetcher()
    {
        var seen = new List<IReadOnlyDictionary<string, string>>();
        var fetcher = new CapturingFetcher(seen);
        var site = Guid.NewGuid();
        var sources = new[]
        {
            Source("OpenMeteo", CallAbout(site, values: new Dictionary<string, string> { ["lat"] = "-25.75" })),
            Source("HazardPortal",
                CallAbout(Guid.NewGuid(), values: new Dictionary<string, string> { ["hazardPortalCode"] = "FL" }),
                CallAbout(Guid.NewGuid(), values: new Dictionary<string, string> { ["hazardPortalCode"] = "WF" })),
        };

        await Runner(fetcher).RunAsync(site, sources, default);

        seen.Should().HaveCount(3);
        seen.Count(values => values.ContainsKey("lat")).Should().Be(1);
        seen.Count(values => values.TryGetValue("hazardPortalCode", out var code) && code == "FL").Should().Be(1);
        seen.Count(values => values.TryGetValue("hazardPortalCode", out var code) && code == "WF").Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_TellsEverySourceWhichSubjectTheCallIsAbout()
    {
        // The fetch writes its reading onto the subject the run names. A source called without it
        // would land its value on whatever Thing the registration's expression names (Bug #6532).
        var fetcher = new ScriptedFetcher(source => Task.FromResult(new SourceOutcome(source, true, null)));
        var site = Guid.NewGuid();
        var assessment = Guid.NewGuid();
        var sources = new[]
        {
            Source("OpenMeteo", CallAbout(site)),
            Source("HazardPortal", CallAbout(assessment, "flood assessment")),
        };

        await Runner(fetcher).RunAsync(site, sources, default);

        fetcher.Subjects.Should().BeEquivalentTo(new[] { site, assessment });
    }

    [Fact]
    public async Task RunAsync_AFailedCallAboutAnotherSubject_NamesItInItsOutcome()
    {
        // A portal answering for five of a site's assessments and not the sixth is reported per
        // assessment; blamed as one failure, the half it did resolve would read as lost too.
        var fetcher = new ScriptedFetcher(name => Task.FromResult(
            new SourceOutcome(name, false, "404: no data for this division and hazardtype")));
        var site = Guid.NewGuid();
        var sources = new[] { Source("HazardPortal", CallAbout(Guid.NewGuid(), "cyclone assessment")) };

        var report = await Runner(fetcher).RunAsync(site, sources, default);

        report.Unresolved.Should().ContainSingle().Which.Subject.Should().Be("cyclone assessment");
    }

    [Fact]
    public async Task RunAsync_ACallAboutTheSiteItself_NamesNoSubject()
    {
        // The report is handed back to the caller that named the site; repeating the site on every
        // ordinary outcome would only invite a reader to trust the copy.
        var fetcher = new ScriptedFetcher(name => Resolved(name));
        var site = Guid.NewGuid();

        var report = await Runner(fetcher).RunAsync(site, SourcesAbout(site, "OpenMeteo"), default);

        report.Resolved.Should().ContainSingle().Which.Subject.Should().BeNull();
    }

    private sealed class CapturingFetcher : ISourceFetcher
    {
        private readonly List<IReadOnlyDictionary<string, string>> _seen;
        public CapturingFetcher(List<IReadOnlyDictionary<string, string>> seen) => _seen = seen;

        public Task<SourceOutcome> FetchAsync(
            Guid subjectId, string sourceName, string endpointName,
            IReadOnlyDictionary<string, string> addressParameters, CancellationToken cancellationToken)
        {
            lock (_seen) _seen.Add(addressParameters);
            return Task.FromResult(new SourceOutcome(sourceName, true, null));
        }
    }
}
