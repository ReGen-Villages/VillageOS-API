using System.Text.RegularExpressions;
using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class RequestBudgetReaderTests
{
    private const string PerVerifiedTicket = "Request budget per verified ticket";
    private const string WithoutATicket = "Request budget without a ticket";

    private static DeclaredModel Budgets(long verifiedCount = 200, long verifiedSeconds = 600, long unverifiedCount = 20, long unverifiedSeconds = 600) =>
        new DeclaredModel()
            .Stating(PerVerifiedTicket,
                (RequestBudgetReader.RequestCountProperty, verifiedCount), (RequestBudgetReader.WindowSecondsProperty, verifiedSeconds))
            .Bound(LandIntakeRoles.VerifiedRequestBudget, PerVerifiedTicket)
            .Stating(WithoutATicket,
                (RequestBudgetReader.RequestCountProperty, unverifiedCount), (RequestBudgetReader.WindowSecondsProperty, unverifiedSeconds))
            .Bound(LandIntakeRoles.UnverifiedRequestBudget, WithoutATicket);

    [Fact]
    public void Both_budgets_are_read_from_the_things_their_roles_bind()
    {
        var read = RequestBudgetReader.Read(Budgets(verifiedCount: 150, verifiedSeconds: 300, unverifiedCount: 15, unverifiedSeconds: 900).Build());

        read.Verified.Should().Be(new RequestBudget(150, TimeSpan.FromSeconds(300)));
        read.Unverified.Should().Be(new RequestBudget(15, TimeSpan.FromSeconds(900)));
        read.FallbackReasons.Should().BeEmpty();
    }

    [Fact]
    public void A_model_binding_no_budget_falls_back_for_each_and_says_why()
    {
        var read = RequestBudgetReader.Read(new DeclaredModel().Build());

        read.Verified.Should().Be(RequestBudget.Fallback);
        read.Unverified.Should().Be(RequestBudget.Fallback);
        read.FallbackReasons.Should().HaveCount(2)
            .And.Contain(reason => Regex.IsMatch(reason, $@"\b{LandIntakeRoles.VerifiedRequestBudget.Word}\b"))
            .And.Contain(reason => Regex.IsMatch(reason, $@"\b{LandIntakeRoles.UnverifiedRequestBudget.Word}\b"));
    }

    [Fact]
    public void A_budget_stating_no_request_count_falls_back()
    {
        var model = new DeclaredModel()
            .Stating(PerVerifiedTicket, (RequestBudgetReader.WindowSecondsProperty, 600L))
            .Bound(LandIntakeRoles.VerifiedRequestBudget, PerVerifiedTicket)
            .Stating(WithoutATicket, (RequestBudgetReader.RequestCountProperty, 20L), (RequestBudgetReader.WindowSecondsProperty, 600L))
            .Bound(LandIntakeRoles.UnverifiedRequestBudget, WithoutATicket);

        var read = RequestBudgetReader.Read(model.Build());

        read.Verified.Should().Be(RequestBudget.Fallback);
        read.FallbackReasons.Should().ContainSingle().Which.Should().Contain(PerVerifiedTicket);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(3_000_000_000L)]
    public void A_request_count_no_limiter_can_hold_falls_back(long count)
    {
        var read = RequestBudgetReader.Read(Budgets(unverifiedCount: count, verifiedCount: 3_000_000_000L).Build());

        read.Unverified.Should().Be(RequestBudget.Fallback);
        read.FallbackReasons.Should().Contain(reason => reason.Contains(WithoutATicket));
    }

    [Fact]
    public void A_verified_budget_below_the_budget_without_a_ticket_falls_back()
    {
        var read = RequestBudgetReader.Read(Budgets(verifiedCount: 10, unverifiedCount: 20).Build());

        read.Verified.Should().Be(RequestBudget.Fallback);
        read.Unverified.Should().Be(new RequestBudget(20, TimeSpan.FromSeconds(600)));
        read.FallbackReasons.Should().ContainSingle().Which.Should().Contain("10").And.Contain("20");
    }

    [Theory]
    [InlineData(59L, 600L)]
    [InlineData(600L, 0L)]
    public void A_window_under_a_minute_falls_back(long verifiedSeconds, long unverifiedSeconds)
    {
        var read = RequestBudgetReader.Read(Budgets(verifiedSeconds: verifiedSeconds, unverifiedSeconds: unverifiedSeconds).Build());

        (verifiedSeconds < 60 ? read.Verified : read.Unverified).Should().Be(RequestBudget.Fallback);
        (verifiedSeconds < 60 ? read.Unverified : read.Verified).Window.Should().Be(TimeSpan.FromSeconds(600));
        read.FallbackReasons.Should().ContainSingle();
    }

    [Fact]
    public void A_role_binding_two_budgets_falls_back_rather_than_choosing_one()
    {
        var model = Budgets()
            .Stating("Another budget", (RequestBudgetReader.RequestCountProperty, 500L), (RequestBudgetReader.WindowSecondsProperty, 600L))
            .Bound(LandIntakeRoles.VerifiedRequestBudget, "Another budget");

        var read = RequestBudgetReader.Read(model.Build());

        read.Verified.Should().Be(RequestBudget.Fallback);
        read.FallbackReasons.Should().ContainSingle().Which.Should().Contain(LandIntakeRoles.VerifiedRequestBudget.Word);
    }

    [Fact]
    public void The_read_asks_for_both_roles_and_what_they_bind()
    {
        var selector = RequestBudgetReader.Selector();

        selector.Ids.Should().BeEquivalentTo(RoleBindings.Identifiers(LandIntakeRoles.VerifiedRequestBudget, LandIntakeRoles.UnverifiedRequestBudget));
        selector.Traverse.Should().ContainSingle().Which.Should().Be(RoleBindings.FollowBindings);
        selector.IncludeRelationships.Should().BeTrue();
    }
}
