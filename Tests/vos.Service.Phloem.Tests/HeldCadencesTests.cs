using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Model;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class HeldCadencesTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Hourly = Guid.NewGuid();

    private static HeldCadences HoldingAnHourlyCadence()
    {
        var held = new HeldCadences();
        held.Replace([new Cadence(Hourly, "Every hour", 60, Nine)]);
        return held;
    }

    private static ModelChangeEvent PropertyChanged(Guid cadenceId, string property, object? value) =>
        new() { Kind = "PropertyChanged", EntityId = cadenceId, PropertyName = property, Value = JsonSerializer.SerializeToElement(value) };

    private static SnapshotThing ACadence(Guid id, string name, double interval, bool isArchetype = false) => new(id, name, isArchetype,
        new Dictionary<string, SnapshotProperty> { [ModelNames.IntervalMinutes] = new(JsonSerializer.SerializeToElement(interval), "vos.Double", null) },
        null, [], null);

    [Fact]
    public async Task A_cadence_edited_on_the_stream_is_due_at_its_new_interval()
    {
        var held = HoldingAnHourlyCadence();

        held.Apply(PropertyChanged(Hourly, ModelNames.IntervalMinutes, 30));

        var cadence = (await held.CurrentAsync(CancellationToken.None)).Single();
        cadence.IntervalMinutes.Should().Be(30);
        CadenceSchedule.DueAt(cadence, Nine.AddMinutes(31), Nine.AddMinutes(29)).Should().Equal(Nine.AddMinutes(30));
    }

    [Fact]
    public async Task The_instant_last_due_follows_the_stream_whether_written_as_text_or_removed()
    {
        var held = HoldingAnHourlyCadence();

        held.Apply(PropertyChanged(Hourly, ModelNames.LastDue, Nine.AddHours(1).ToString("o")));
        (await held.CurrentAsync(CancellationToken.None)).Single().LastDue.Should().Be(Nine.AddHours(1));

        held.Apply(new ModelChangeEvent { Kind = "PropertyDeleted", EntityId = Hourly, PropertyName = ModelNames.LastDue });
        (await held.CurrentAsync(CancellationToken.None)).Single().LastDue.Should().BeNull();
    }

    [Fact]
    public async Task A_cadence_typed_into_the_kind_later_is_held_and_one_typed_out_or_deleted_is_let_go()
    {
        var held = HoldingAnHourlyCadence();
        var daily = Guid.NewGuid();

        held.Apply(new ModelChangeEvent { Kind = "ThingEntered", EntityId = daily, Thing = ACadence(daily, "Every day", 1440) });
        (await held.CurrentAsync(CancellationToken.None)).Should().Contain(new Cadence(daily, "Every day", 1440, null));

        held.Apply(new ModelChangeEvent { Kind = "ThingLeft", EntityId = daily });
        (await held.CurrentAsync(CancellationToken.None)).Should().NotContain(cadence => cadence.Id == daily);

        held.Apply(new ModelChangeEvent { Kind = "ThingDeleted", EntityId = Hourly });
        (await held.CurrentAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_kind_of_cadence_entering_is_not_held_as_one()
    {
        var held = HoldingAnHourlyCadence();
        var kind = Guid.NewGuid();

        held.Apply(new ModelChangeEvent { Kind = "ThingEntered", EntityId = kind, Thing = ACadence(kind, "Weekly cadences", 0, isArchetype: true) });

        (await held.CurrentAsync(CancellationToken.None)).Should().ContainSingle(cadence => cadence.Id == Hourly);
    }

    [Fact]
    public async Task A_change_to_a_Thing_not_held_changes_nothing()
    {
        var held = HoldingAnHourlyCadence();

        held.Apply(PropertyChanged(Guid.NewGuid(), ModelNames.IntervalMinutes, 5));
        held.Apply(new ModelChangeEvent { Kind = "ThingLeft", EntityId = Guid.NewGuid() });

        (await held.CurrentAsync(CancellationToken.None)).Should().Equal(new Cadence(Hourly, "Every hour", 60, Nine));
    }

    [Fact]
    public async Task The_instant_marked_due_moves_the_held_cadence_at_once()
    {
        var held = HoldingAnHourlyCadence();

        held.MarkDue(Hourly, Nine.AddHours(2));

        (await held.CurrentAsync(CancellationToken.None)).Single().LastDue.Should().Be(Nine.AddHours(2));
    }

    [Fact]
    public async Task Nothing_is_answered_before_the_first_read_and_a_failed_read_is_answered_until_one_succeeds()
    {
        var held = new HeldCadences();
        var waiting = held.CurrentAsync(CancellationToken.None);
        waiting.IsCompleted.Should().BeFalse();

        held.NoteFailure(new HttpRequestException("the broker is away"));

        (await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<HttpRequestException>()).WithMessage("the broker is away");
        await FluentActions.Awaiting(() => held.CurrentAsync(CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();

        held.Replace([new Cadence(Hourly, "Every hour", 60, null)]);
        (await held.CurrentAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_read_replaces_what_was_held_and_releases_the_looks_waiting_for_it()
    {
        var held = HoldingAnHourlyCadence();
        var fresh = new HeldCadences();
        var waiting = fresh.CurrentAsync(CancellationToken.None);

        held.Replace([new Cadence(Hourly, "Every hour", 30, null)]);
        fresh.Replace([]);

        (await held.CurrentAsync(CancellationToken.None)).Should().Equal(new Cadence(Hourly, "Every hour", 30, null));
        (await waiting).Should().BeEmpty();
    }

    [Fact]
    public void A_cadence_is_read_off_a_snapshot_Thing_wherever_it_states_its_values()
    {
        var kind = Guid.NewGuid();
        var inheriting = new SnapshotThing(Hourly, "Every hour", false, new Dictionary<string, SnapshotProperty>(),
            new Dictionary<string, InheritedPropertySet>
            {
                [kind.ToString()] = new("Cadence", new Dictionary<string, SnapshotProperty>
                {
                    [ModelNames.IntervalMinutes] = new(JsonSerializer.SerializeToElement("60"), "vos.String", null),
                    [ModelNames.LastDue] = new(JsonSerializer.SerializeToElement(Nine.ToString("o")), "vos.DateTime", null),
                }, null),
            }, [], null);

        HeldCadences.CadenceOf(inheriting).Should().Be(new Cadence(Hourly, "Every hour", 60, Nine));
        HeldCadences.CadenceOf(ACadence(Hourly, "Every hour", 1440)).Should().Be(new Cadence(Hourly, "Every hour", 1440, null));
    }
}
