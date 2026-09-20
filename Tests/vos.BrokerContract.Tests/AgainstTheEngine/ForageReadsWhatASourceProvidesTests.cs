using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

/// <summary>
/// Forage's read of a site, resolved by the real engine from the selector the service sends, rather
/// than a snapshot a test built holding every Thing.
///
/// The resolver composes a source's <c>variables</c> from the Things it reaches through the predicate
/// marked as providing them, reading the predicate and the variables out of the snapshot it was handed.
/// Its unit tests hand it one holding all of them, so a selector that asked for none passed every case
/// while every live discovery run called the climate history with the placeholder unfilled and was
/// answered 400 (Bug #7192). This is the case that could not have.
/// </summary>
public class ForageReadsWhatASourceProvidesTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageReadsWhatASourceProvidesTests(TheEngine engine) => _engine = engine;

    private SubscriptionClient Subscriptions() => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);

    [Fact]
    public async Task A_site_read_reaches_the_variables_a_source_provides_and_the_predicate_it_provides_them_through()
    {
        var isIn = await _engine.DeclareOnceAsync(CoveringSourceResolver.IsInPredicate);
        var covers = await _engine.DeclareOnceAsync(CoveringSourceResolver.CoversPredicate);
        var resolvedBy = await _engine.DeclareOnceAsync(CoveringSourceResolver.ResolvedByPredicate);
        var provides = await _engine.DeclareOnceAsync("provides",
            properties: new Dictionary<string, object?> { [CoveringSourceResolver.ProvidedVariablePredicateFlag] = true });

        var place = await _engine.DeclareAsync("Portugal");
        var site = await _engine.DeclareAsync("Willow Bend");
        var source = await _engine.DeclareAsync("Climate history");
        var registration = await _engine.DeclareAsync("climate-history");
        var temperature = await _engine.DeclareAsync("temperatureCelsius",
            properties: new Dictionary<string, object?> { [CoveringSourceResolver.ProviderNameProperty] = "temperature_2m" });
        var rain = await _engine.DeclareAsync("rainMillimetres",
            properties: new Dictionary<string, object?> { [CoveringSourceResolver.ProviderNameProperty] = "rain" });

        await _engine.RelateAsync(site, isIn, place);
        await _engine.RelateAsync(source, covers, place);
        await _engine.RelateAsync(source, resolvedBy, registration);
        await _engine.RelateAsync(source, provides, temperature);
        await _engine.RelateAsync(source, provides, rain);

        var subscribed = await Subscriptions().SubscribeAsync(CoveringSourceResolver.SelectorFor(site));
        var covering = CoveringSourceResolver.Resolve(subscribed.Snapshot, site);

        covering.Should().ContainSingle().Which.Calls.Should().ContainSingle()
            .Which.Values.Should().Contain(CoveringSourceResolver.VariablesPlaceholder, "rain,temperature_2m");
    }
}
