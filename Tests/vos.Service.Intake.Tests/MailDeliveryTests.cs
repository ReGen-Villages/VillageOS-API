using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Intake;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>How a verification code leaves this service. A deployment hands it to a mail server; a
/// developer with no relay to hand can have it written where they can read it instead, which verifies
/// nobody and is refused anywhere but a development machine.</summary>
public class MailDeliveryTests
{
    private static IConfiguration Configured(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static readonly string[] Relay =
        ["--mailHost=smtp.example.test", "--mailFrom=intake@example.test"];

    [Fact]
    public void A_service_told_nothing_about_delivery_sends_through_a_server()
    {
        var delivery = MailDelivery.Parse(Relay, Configured());

        delivery!.IsToTheConsole.Should().BeFalse();
        delivery.Server.Should().Be(new MailSettings("smtp.example.test", 587, "intake@example.test", null, null));
    }

    [Fact]
    public void Naming_a_server_outright_is_what_it_already_did()
    {
        var delivery = MailDelivery.Parse([.. Relay, $"--mailDelivery={MailDelivery.ToAServer}"], Configured());

        delivery!.Server.Should().Be(MailDelivery.Parse(Relay, Configured())!.Server);
    }

    // The whole point: a developer has no relay, so asking for one would be asking for what they came
    // here without.
    [Fact]
    public void The_console_asks_for_no_server_no_address_and_no_account()
    {
        var delivery = MailDelivery.Parse([$"--mailDelivery={MailDelivery.ToTheConsole}"], Configured());

        delivery!.IsToTheConsole.Should().BeTrue();
        delivery.Server.Should().BeNull();
    }

    [Fact]
    public void The_console_is_asked_for_the_same_way_through_configuration()
    {
        var delivery = MailDelivery.Parse([], Configured(("MailDelivery", MailDelivery.ToTheConsole)));

        delivery!.IsToTheConsole.Should().BeTrue();
    }

    // A way of delivering nobody implements is a mistake, not a request for the default. Falling back
    // would start the service sending through a server the operator did not ask for, and a typo would
    // read as working.
    [Fact]
    public void A_way_of_delivering_this_service_does_not_have_stops_it_rather_than_defaulting()
    {
        MailDelivery.Parse([.. Relay, "--mailDelivery=carrier-pigeon"], Configured()).Should().BeNull();
    }

    [Fact]
    public void Asking_for_a_server_without_saying_which_is_no_delivery_at_all()
    {
        MailDelivery.Parse([$"--mailDelivery={MailDelivery.ToAServer}"], Configured()).Should().BeNull();
    }

    // A code written where it can be read was received by nobody, so the route it guards proves nothing
    // about who reads the address on a submission.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Writing_codes_where_they_can_be_read_is_refused_off_a_development_machine(string environment)
    {
        var delivery = MailDelivery.Parse([$"--mailDelivery={MailDelivery.ToTheConsole}"], Configured())!;

        delivery.WhyRefusedIn(environment).Should().NotBeNull()
            .And.Subject.ToString().Should().Contain(MailDelivery.ToTheConsole);
    }

    [Fact]
    public void On_a_development_machine_it_is_allowed()
    {
        var delivery = MailDelivery.Parse([$"--mailDelivery={MailDelivery.ToTheConsole}"], Configured())!;

        delivery.WhyRefusedIn("Development").Should().BeNull();
    }

    [Fact]
    public void Sending_through_a_server_is_refused_nowhere()
    {
        var delivery = MailDelivery.Parse(Relay, Configured())!;

        delivery.WhyRefusedIn("Production").Should().BeNull();
    }

    [Fact]
    public void The_usage_message_names_the_setting_and_both_of_its_answers()
    {
        MailDelivery.UsageMessage.Should()
            .Contain("--mailDelivery").And
            .Contain(MailDelivery.ToAServer).And
            .Contain(MailDelivery.ToTheConsole);
    }
}
