using FluentAssertions;
using vos.Service.Intake;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>The codes this service is waiting for, and what it will accept back. Anybody may ask for one,
/// so every bound here is a bound on what a stranger can make this service do.</summary>
public class AddressVerificationTests
{
    private const string Address = "ana.ferreira@example.pt";

    private static MovableClock AClock() => new(new DateTimeOffset(2026, 8, 22, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public void The_code_that_was_sent_is_the_one_accepted()
    {
        var verification = new AddressVerification(AClock());

        var code = verification.CodeFor(Address)!;

        verification.WhyRefused(Address, code).Should().BeNull();
    }

    // Asking again is ordinary — the first did not arrive, or went to spam. The last one sent is the one
    // that works, so a person reading the newest message is not turned away by it.
    [Fact]
    public void Asking_again_replaces_the_code_that_was_outstanding()
    {
        var verification = new AddressVerification(AClock());

        var first = verification.CodeFor(Address)!;
        var second = verification.CodeFor(Address)!;

        verification.WhyRefused(Address, second).Should().BeNull();
        verification.WhyRefused(Address, first).Should().NotBeNull();
    }

    [Fact]
    public void A_code_older_than_it_lasts_is_refused()
    {
        var clock = AClock();
        var verification = new AddressVerification(clock);
        var code = verification.CodeFor(Address)!;

        clock.Advance(AddressVerification.ValidFor + TimeSpan.FromMinutes(1));

        verification.WhyRefused(Address, code).Should().NotBeNull();
    }

    [Fact]
    public void A_code_is_spent_once_it_has_been_answered()
    {
        var verification = new AddressVerification(AClock());
        var code = verification.CodeFor(Address)!;

        verification.WhyRefused(Address, code).Should().BeNull();

        verification.WhyRefused(Address, code).Should()
            .NotBeNull("a code that went on working would be a ticket anybody who saw it could keep taking");
    }

    [Fact]
    public void A_code_answered_wrongly_too_often_stops_working()
    {
        var verification = new AddressVerification(AClock());
        var code = verification.CodeFor(Address)!;

        for (var guess = 0; guess < AddressVerification.AnswersAllowed; guess++)
            verification.WhyRefused(Address, "000000");

        verification.WhyRefused(Address, code).Should()
            .NotBeNull("guessing six figures has to run out rather than merely be unlikely");
    }

    [Fact]
    public void An_address_is_sent_only_so_many_codes_in_a_window()
    {
        var verification = new AddressVerification(AClock());

        for (var asked = 0; asked < AddressVerification.CodesPerAddress; asked++)
            verification.CodeFor(Address).Should().NotBeNull();

        verification.CodeFor(Address).Should()
            .BeNull("a route that sends on demand is a way to post to a mailbox its owner never gave us");
    }

    // The budget is what somebody with a genuine reason to ask again comes back to, so it has to run out
    // rather than close the address for good.
    [Fact]
    public void An_address_may_be_asked_about_again_once_the_window_has_passed()
    {
        var clock = AClock();
        var verification = new AddressVerification(clock);
        for (var asked = 0; asked < AddressVerification.CodesPerAddress; asked++)
            verification.CodeFor(Address);

        clock.Advance(AddressVerification.CodeBudgetWindow + TimeSpan.FromMinutes(1));

        verification.CodeFor(Address).Should().NotBeNull();
    }

    // Spending the budget and waiting for the code to die must not leave the address held for ever, or a
    // stranger asking about a fresh address each time fills memory a request at a time.
    [Fact]
    public void What_is_held_for_an_address_is_let_go_once_neither_half_still_applies()
    {
        var clock = AClock();
        var verification = new AddressVerification(clock);
        for (var address = 0; address < AddressVerification.MostAddressesHeld; address++)
            verification.CodeFor($"asker-{address}@example.pt");

        clock.Advance(AddressVerification.CodeBudgetWindow + AddressVerification.ValidFor);
        var code = verification.CodeFor(Address)!;

        verification.WhyRefused(Address, code).Should()
            .BeNull("the one asked about after the rest were let go is held like any other");
    }

    // A mail server having a bad afternoon must not spend the budget on codes nobody could read. Without
    // this, three failures nobody saw lock the address's owner out for the hour.
    [Fact]
    public void A_code_that_never_left_the_service_costs_the_address_nothing()
    {
        var verification = new AddressVerification(AClock());

        for (var failed = 0; failed < AddressVerification.CodesPerAddress * 2; failed++)
        {
            verification.CodeFor(Address).Should().NotBeNull();
            verification.NothingWasSent(Address);
        }

        verification.CodeFor(Address).Should().NotBeNull();
    }

    [Fact]
    public void A_code_that_never_left_the_service_cannot_be_answered()
    {
        var verification = new AddressVerification(AClock());
        var code = verification.CodeFor(Address)!;

        verification.NothingWasSent(Address);

        verification.WhyRefused(Address, code).Should().Be(AddressVerification.NotTheCode);
    }

    [Fact]
    public void Taking_back_a_code_for_an_address_nothing_was_sent_to_does_nothing()
    {
        var verification = new AddressVerification(AClock());

        var act = () => verification.NothingWasSent("somebody.else@example.pt");

        act.Should().NotThrow();
    }

    // Nothing has expired here, so what makes room is the cap rather than the sweep. Without it a
    // stranger asking about a fresh address every few seconds grows this a request at a time.
    [Fact]
    public void The_oldest_is_let_go_once_the_cap_is_reached_with_nothing_yet_expired()
    {
        var clock = AClock();
        var verification = new AddressVerification(clock);
        var first = verification.CodeFor("asker-0@example.pt")!;

        for (var address = 1; address <= AddressVerification.MostAddressesHeld; address++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            verification.CodeFor($"asker-{address}@example.pt");
        }

        verification.WhyRefused("asker-0@example.pt", first).Should()
            .NotBeNull("the room a stranger can take has to be fixed, and the oldest is what goes");
    }

    // Case and surrounding space are not part of an address anybody meant, and the code goes to the
    // mailbox either spelling names.
    [Theory]
    [InlineData("  ana.ferreira@example.pt  ")]
    [InlineData("Ana.Ferreira@Example.PT")]
    public void A_code_is_answerable_under_any_spelling_of_the_address_it_went_to(string spelledOtherwise)
    {
        var verification = new AddressVerification(AClock());
        var code = verification.CodeFor(Address)!;

        verification.WhyRefused(spelledOtherwise, code).Should().BeNull();
    }

    // A caller names the address. Were a wrong code worded differently from an address nothing was sent
    // to, anybody could ask whether somebody had just started a submission under any address they cared
    // to type.
    [Fact]
    public void An_address_nothing_was_sent_to_is_refused_in_the_same_words_as_a_wrong_code()
    {
        var verification = new AddressVerification(AClock());
        verification.CodeFor(Address);

        var wrongCode = verification.WhyRefused(Address, "000000");
        var neverAskedAbout = verification.WhyRefused("somebody.else@example.pt", "000000");

        neverAskedAbout.Should().Be(wrongCode).And.NotBeNull();
        neverAskedAbout.Should().NotContain("somebody.else@example.pt");
    }

    // Reachable only after the attempts on a live code have been spent, which is itself a state a caller
    // must not be able to read off the wording.
    [Fact]
    public void A_code_run_out_of_answers_is_refused_in_those_same_words()
    {
        var verification = new AddressVerification(AClock());
        var code = verification.CodeFor(Address)!;
        for (var guess = 0; guess < AddressVerification.AnswersAllowed; guess++)
            verification.WhyRefused(Address, "000000");

        verification.WhyRefused(Address, code).Should().Be(AddressVerification.NotTheCode);
    }
}
