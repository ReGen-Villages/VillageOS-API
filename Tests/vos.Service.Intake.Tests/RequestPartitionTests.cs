using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace vos.Service.Intake.Tests;

public class RequestPartitionTests
{
    private const string Ana = "ana.ferreira@example.pt";
    private const string Source = "203.0.113.5";

    private static readonly RequestBudget Verified = new(200, TimeSpan.FromSeconds(600));
    private static readonly RequestBudget Unverified = new(20, TimeSpan.FromSeconds(600));
    private static readonly DeclaredRequestBudgets Budgets = new(Verified, Unverified, []);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Two_tickets_issued_for_one_address_carry_one_mark()
    {
        var tickets = new SubmissionTicket(_clock);
        var first = tickets.Issue(Ana);
        _clock.Advance(TimeSpan.FromMinutes(3));
        var renewed = tickets.Issue("  Ana.Ferreira@Example.PT ");

        renewed.Should().NotBe(first);
        tickets.AddressMarkOf(renewed).Should().NotBeNull().And.Be(tickets.AddressMarkOf(first));
    }

    [Fact]
    public void Tickets_for_two_addresses_carry_two_marks()
    {
        var tickets = new SubmissionTicket(_clock);

        tickets.AddressMarkOf(tickets.Issue(Ana)).Should().NotBe(tickets.AddressMarkOf(tickets.Issue("somebody.else@example.pt")));
    }

    [Fact]
    public void A_mark_does_not_carry_the_address()
    {
        var tickets = new SubmissionTicket(_clock);

        tickets.AddressMarkOf(tickets.Issue(Ana)).Should().NotContainEquivalentOf("ana");
    }

    public static TheoryData<string, Func<SubmissionTicket, FakeTimeProvider, string?>> NoMark => new()
    {
        { "no ticket", (_, _) => null },
        { "an empty ticket", (_, _) => "" },
        { "a value that is not a ticket", (_, _) => "not-a-ticket" },
        { "a ticket another instance signed", (_, clock) => new SubmissionTicket(clock).Issue(Ana) },
        {
            "an expired ticket", (tickets, clock) =>
            {
                var issued = tickets.Issue(Ana);
                clock.Advance(SubmissionTicket.ValidFor + TimeSpan.FromSeconds(1));
                return issued;
            }
        },
    };

    [Theory]
    [MemberData(nameof(NoMark))]
    public void A_ticket_that_would_be_refused_carries_no_mark(string why, Func<SubmissionTicket, FakeTimeProvider, string?> presented)
    {
        var tickets = new SubmissionTicket(_clock);

        tickets.AddressMarkOf(presented(tickets, _clock)).Should().BeNull(why);
    }

    [Fact]
    public void A_live_ticket_is_counted_against_its_address_under_the_verified_budget()
    {
        var tickets = new SubmissionTicket(_clock);
        var ticket = tickets.Issue(Ana);

        var (key, budget) = RequestPartition.For(ticket, Source, tickets, Budgets);

        budget.Should().Be(Verified);
        key.Should().Contain(tickets.AddressMarkOf(ticket)!).And.NotContain(Source);
    }

    [Theory]
    [MemberData(nameof(NoMark))]
    public void Any_other_request_is_counted_against_its_source_under_the_budget_without_a_ticket(
        string why, Func<SubmissionTicket, FakeTimeProvider, string?> presented)
    {
        var tickets = new SubmissionTicket(_clock);

        var (key, budget) = RequestPartition.For(presented(tickets, _clock), Source, tickets, Budgets);

        budget.Should().Be(Unverified, why);
        key.Should().Contain(Source, why);
    }

    [Fact]
    public void A_ticketed_request_and_one_without_from_the_same_source_are_counted_apart()
    {
        var tickets = new SubmissionTicket(_clock);

        RequestPartition.For(tickets.Issue(Ana), Source, tickets, Budgets).Key
            .Should().NotBe(RequestPartition.For(null, Source, tickets, Budgets).Key);
    }

    // A fixed-window partition keeps the options it was made with, so only a new key can carry a changed budget.
    [Fact]
    public void A_changed_budget_gives_a_new_key()
    {
        var tickets = new SubmissionTicket(_clock);
        var ticket = tickets.Issue(Ana);
        var raised = Budgets with { Verified = new RequestBudget(300, TimeSpan.FromSeconds(600)) };

        RequestPartition.For(ticket, Source, tickets, raised).Key.Should().NotBe(RequestPartition.For(ticket, Source, tickets, Budgets).Key);
        RequestPartition.For(null, Source, tickets, raised).Key.Should().Be(RequestPartition.For(null, Source, tickets, Budgets).Key);
    }

    [Fact]
    public void A_request_with_no_address_is_one_source_like_any_other()
    {
        var tickets = new SubmissionTicket(_clock);

        RequestPartition.For(null, null, tickets, Budgets).Key.Should().Be(RequestPartition.For(null, null, tickets, Budgets).Key);
        RequestPartition.For(null, null, tickets, Budgets).Budget.Should().Be(Unverified);
    }
}
