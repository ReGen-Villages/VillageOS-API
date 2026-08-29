using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>Which mailer a service actually ends up holding. The settings decide it and the host wires
/// it, and a test of the settings alone would pass while the host sent through a server nobody
/// configured.</summary>
public class MailerWiringTests
{
    private static IntakeWebApplicationFactory Started(string environment, string? delivery) => new()
    {
        Environment = environment,
        MailDelivery = delivery,
        KeepsTheServicesOwnMailer = true,
    };

    [Fact]
    public async Task A_service_told_to_write_codes_to_the_console_holds_that_mailer()
    {
        await using var factory = Started("Development", MailDelivery.ToTheConsole);
        using var _ = factory.CreateClient();

        factory.Services.GetRequiredService<IVerificationMailer>()
            .Should().BeOfType<ConsoleVerificationMailer>();
    }

    [Fact]
    public async Task A_service_told_nothing_about_delivery_holds_the_one_that_sends()
    {
        await using var factory = Started("Development", delivery: null);
        using var _ = factory.CreateClient();

        factory.Services.GetRequiredService<IVerificationMailer>()
            .Should().BeOfType<SmtpVerificationMailer>();
    }

    // Asking for the console anywhere else stops the service rather than wiring anything, so there is no
    // host left to ask what it holds. That refusal is MailDelivery.WhyRefusedIn, and it is tested there
    // for the same reason the usage-and-stop path already is: this host cannot be started wrong without
    // taking the test run with it.
}
