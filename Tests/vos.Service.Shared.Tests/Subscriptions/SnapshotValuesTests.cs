using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Shared.Tests.Subscriptions;

/// <summary>Reading a snapshot the way Mycelium sends one.
///
/// <para>Every other test of a snapshot reader builds the records in memory, which asserts nothing about
/// the names the payload arrives under. These deserialize the payload instead, so a member that binds to
/// nothing fails here rather than on a running broker — which is how a value every submitted site states
/// read as absent to every service while reading back perfectly well over the REST routes (#6805).</para>
///
/// <para>The payload below is what <c>SnapshotBuilder</c> produces for a Thing that wrote a value for a
/// name its archetype declares, serialized as Mycelium's controllers serialize it: property names keep
/// the casing the platform declared them in.</para></summary>
public class SnapshotValuesTests
{
    private const string SubscribeResponse = """
    {
      "subscriptionId": "8b23b2b2-3c6a-4a2f-9a2f-1f8d5b6e4c31",
      "watermark": 7,
      "snapshot": {
        "watermark": 7,
        "things": [
          {
            "Id": "fefab87a-4830-480a-93b1-623ba440de6c",
            "Name": "Willow Bend Parcel-01",
            "IsArchetype": false,
            "Properties": {},
            "RollupProperties": null,
            "InheritedOverrides": {
              "6513d4d5-c6d1-47c9-babd-3256848f79d9": {
                "SourceId": "6513d4d5-c6d1-47c9-babd-3256848f79d9",
                "SourceName": "Parcel",
                "InheritedAt": "2026-08-28T17:41:22.289038Z",
                "Properties": {
                  "measuredAreaHectares": { "typeInfo": "vos.Double", "value": 28.39 }
                },
                "Inherited": null
              }
            },
            "States": [],
            "Relationships": ["36fa3bce-77b1-4a64-a76e-61a3c5c4b613"]
          },
          {
            "Id": "6513d4d5-c6d1-47c9-babd-3256848f79d9",
            "Name": "Parcel",
            "IsArchetype": true,
            "Properties": {
              "measuredAreaHectares": { "typeInfo": "vos.Double", "value": null }
            },
            "RollupProperties": null,
            "InheritedOverrides": null,
            "States": [],
            "Relationships": ["36fa3bce-77b1-4a64-a76e-61a3c5c4b613"]
          }
        ],
        "relationships": [
          {
            "Id": "36fa3bce-77b1-4a64-a76e-61a3c5c4b613",
            "Name": null,
            "SubjectId": "fefab87a-4830-480a-93b1-623ba440de6c",
            "PredicateId": "c7e8ab11-6374-4856-89b2-d516d96224a4",
            "TargetId": "6513d4d5-c6d1-47c9-babd-3256848f79d9",
            "Properties": {},
            "InheritedOverrides": null,
            "States": []
          }
        ]
      }
    }
    """;

    // The options SubscriptionClient reads a response with, so what binds here binds there.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static SnapshotThing Parcel() =>
        JsonSerializer.Deserialize<SubscribeResult>(SubscribeResponse, Json)!
            .Snapshot.Things.Single(thing => !thing.IsArchetype);

    [Fact]
    public void A_value_stated_over_an_archetypes_declaration_is_read_from_the_payload()
    {
        Parcel().StatedValue("measuredAreaHectares")!.Value.GetDouble().Should().Be(28.39);
    }

    /// <summary>Where the value is carried, in the words the payload uses. The value above could be found
    /// through a member bound to anything, and this is what says which one.</summary>
    [Fact]
    public void The_payload_carries_it_as_an_override_and_not_among_the_things_own_properties()
    {
        var parcel = Parcel();

        parcel.Properties.Should().BeEmpty();
        parcel.InheritedOverrides.Should().ContainSingle()
            .Which.Value.Properties.Should().ContainKey("measuredAreaHectares");
    }

    /// <summary>A Thing that overrode nothing carries no override set at all, so every reader has to
    /// answer for the member being absent rather than empty.</summary>
    [Fact]
    public void A_thing_that_states_nothing_over_a_declaration_is_read_without_faulting()
    {
        var archetype = JsonSerializer.Deserialize<SubscribeResult>(SubscribeResponse, Json)!
            .Snapshot.Things.Single(thing => thing.IsArchetype);

        archetype.InheritedOverrides.Should().BeNull();
        archetype.StatedValue("nothingStatesThis").Should().BeNull();
        archetype.ValuesStated().Select(stated => stated.Key).Should().Equal("measuredAreaHectares");
    }

    [Fact]
    public void Every_value_a_thing_states_is_read_whichever_of_the_two_it_is_carried_in()
    {
        var stated = Parcel().ValuesStated().ToDictionary(entry => entry.Key, entry => entry.Value);

        stated.Keys.Should().Equal("measuredAreaHectares");
        stated["measuredAreaHectares"].Value.GetDouble().Should().Be(28.39);
    }

    /// <summary>An override two levels up: a name declared on the archetype's own archetype is stored
    /// under a set nested inside the first, and a walk of the top level alone would miss it.</summary>
    [Fact]
    public void A_value_stated_over_a_declaration_further_up_the_chain_is_read()
    {
        var stated = new SnapshotProperty(
            JsonDocument.Parse("41.5").RootElement, "vos.Double", null);
        var thing = new SnapshotThing(
            Guid.NewGuid(), "Willow Bend Parcel-01", false,
            new Dictionary<string, SnapshotProperty>(),
            new Dictionary<string, InheritedPropertySet>
            {
                ["Parcel"] = new("Parcel", new Dictionary<string, SnapshotProperty>(),
                    new Dictionary<string, InheritedPropertySet>
                    {
                        ["SiteAttachment"] = new("SiteAttachment",
                            new Dictionary<string, SnapshotProperty> { ["measuredAreaHectares"] = stated }, null),
                    }),
            },
            [], []);

        thing.StatedValue("measuredAreaHectares")!.Value.GetDouble().Should().Be(41.5);
        thing.ValuesStated().Select(entry => entry.Key).Should().Equal("measuredAreaHectares");
    }

    /// <summary>An own property answers for a name an override set also holds, so a name read twice is
    /// read once and answers the same way both times.</summary>
    [Fact]
    public void An_own_property_answers_for_a_name_an_override_set_also_holds()
    {
        var own = new SnapshotProperty(JsonDocument.Parse("1").RootElement, "vos.Double", null);
        var overridden = new SnapshotProperty(JsonDocument.Parse("2").RootElement, "vos.Double", null);
        var thing = new SnapshotThing(
            Guid.NewGuid(), "Willow Bend Parcel-01", false,
            new Dictionary<string, SnapshotProperty> { ["measuredAreaHectares"] = own },
            new Dictionary<string, InheritedPropertySet>
            {
                ["Parcel"] = new("Parcel",
                    new Dictionary<string, SnapshotProperty> { ["measuredAreaHectares"] = overridden }, null),
            },
            [], []);

        thing.StatedValue("measuredAreaHectares")!.Value.GetDouble().Should().Be(1);
        thing.ValuesStated().Should().ContainSingle().Which.Value.Value.GetDouble().Should().Be(1);
    }
}
