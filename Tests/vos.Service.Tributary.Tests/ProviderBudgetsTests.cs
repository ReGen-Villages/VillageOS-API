using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class ProviderBudgetsTests
{
    private static readonly Guid Registration = Guid.NewGuid();
    private static readonly Guid Template = Guid.NewGuid();
    private static readonly Guid Licence = Guid.NewGuid();
    private static readonly Guid PerSecond = Guid.NewGuid();
    private static readonly Guid PerDay = Guid.NewGuid();
    private static readonly Guid IsPredicate = Guid.NewGuid();
    private static readonly Guid BindsPredicate = Guid.NewGuid();
    private static readonly Guid UsedUnder = Guid.NewGuid();
    private static readonly Guid Holds = Guid.NewGuid();

    private static SnapshotThing Thing(Guid id, string name, params (string Name, string Value)[] stated) =>
        new(id, name, false,
            stated.ToDictionary(value => value.Name, value => new SnapshotProperty(JsonDocument.Parse(value.Value).RootElement, null, null)),
            new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []);

    private static SnapshotDocument Snapshot(
        IEnumerable<SnapshotThing>? budgets = null, Guid? licenceHolder = null, bool bindsTheRoles = true) =>
        new(0,
        [
            Thing(Registration, "open-topo-data-elevation"),
            Thing(Template, "PositionLookupEndpoint"),
            Thing(Licence, "OpenTopoDataAndSrtmTerms"),
            Thing(IsPredicate, "is"),
            Thing(BindsPredicate, RoleBindings.BindsPredicateName),
            Thing(UsedUnder, "isUsedUnder"),
            Thing(Holds, "holds"),
            Thing(LandIntakeRoles.Licence.Id, "Licence"),
            Thing(LandIntakeRoles.Holding.Id, "Holding"),
            .. budgets ??
            [
                Thing(PerSecond, "OpenTopoDataBudgetPerSecond", ("requestCount", "1"), ("windowSeconds", "1")),
                Thing(PerDay, "OpenTopoDataBudgetPerDay", ("requestCount", "1000"), ("windowSeconds", "86400")),
            ],
        ],
        [
            Edge(Registration, IsPredicate, Template),
            Edge(licenceHolder ?? Registration, UsedUnder, Licence),
            Edge(Licence, Holds, PerSecond),
            Edge(Licence, Holds, PerDay),
            .. bindsTheRoles
                ? [Edge(LandIntakeRoles.Licence.Id, BindsPredicate, UsedUnder), Edge(LandIntakeRoles.Holding.Id, BindsPredicate, Holds)]
                : Array.Empty<SnapshotRelationship>(),
        ]);

    [Fact]
    public void A_registration_reaches_its_licences_budgets_through_the_two_roles_whatever_the_predicates_are_called()
    {
        var read = ProviderBudgets.Resolve(Snapshot(), Registration);

        read.UnreadableBudgetName.Should().BeNull();
        read.Budgets.Should().BeEquivalentTo(new[]
        {
            new ProviderBudget(PerSecond, "OpenTopoDataBudgetPerSecond", 1, TimeSpan.FromSeconds(1)),
            new ProviderBudget(PerDay, "OpenTopoDataBudgetPerDay", 1000, TimeSpan.FromDays(1)),
        });
    }

    [Fact]
    public void A_licence_on_the_template_counts_for_the_registration_beneath_it()
    {
        ProviderBudgets.Resolve(Snapshot(licenceHolder: Template), Registration).Budgets.Should().HaveCount(2);
    }

    [Fact]
    public void A_registration_reaching_no_licence_has_no_budgets()
    {
        ProviderBudgets.Resolve(Snapshot(licenceHolder: Guid.NewGuid()), Registration).Budgets.Should().BeEmpty();
    }

    [Fact]
    public void A_model_binding_neither_role_has_no_budgets()
    {
        ProviderBudgets.Resolve(Snapshot(bindsTheRoles: false), Registration).Budgets.Should().BeEmpty();
    }

    [Fact]
    public void A_held_Thing_stating_neither_a_count_nor_a_window_is_not_a_budget()
    {
        var read = ProviderBudgets.Resolve(Snapshot([Thing(PerSecond, "Logo")]), Registration);

        read.Budgets.Should().BeEmpty();
        read.UnreadableBudgetName.Should().BeNull();
    }

    [Theory]
    [InlineData("requestCount")]
    [InlineData("windowSeconds")]
    public void A_budget_stating_one_value_without_the_other_cannot_be_read(string stated)
    {
        var read = ProviderBudgets.Resolve(Snapshot([Thing(PerSecond, "OpenTopoDataBudgetPerSecond", (stated, "1"))]), Registration);

        read.UnreadableBudgetName.Should().Be("OpenTopoDataBudgetPerSecond");
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("-1", "1")]
    [InlineData("\"one\"", "1")]
    public void A_budget_whose_count_or_window_is_not_above_nothing_cannot_be_read(string count, string seconds)
    {
        var read = ProviderBudgets.Resolve(
            Snapshot([Thing(PerSecond, "OpenTopoDataBudgetPerSecond", ("requestCount", count), ("windowSeconds", seconds))]),
            Registration);

        read.UnreadableBudgetName.Should().Be("OpenTopoDataBudgetPerSecond");
    }
}
