using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>The mailer for a developer with nowhere to send. It writes down the two things §12 of
/// docs/LAND_INTAKE.md keeps out of a log — an address somebody typed, and the code that would let
/// anybody submit under it — which is the whole reason it is refused off a development machine.</summary>
public class ConsoleVerificationMailerTests
{
    [Fact]
    public async Task The_code_is_written_where_whoever_started_the_service_reads_it()
    {
        var log = new CapturingLogger<ConsoleVerificationMailer>();

        await new ConsoleVerificationMailer(log).SendAsync("ana.ferreira@example.pt", "314159", default);

        log.Lines.Should().ContainSingle()
            .Which.Should().Contain("314159").And.Contain("ana.ferreira@example.pt");
    }

    // Whoever reads the log has to see that nothing was sent, or a developer waits for mail that is never
    // coming and a deployment reads as working.
    [Fact]
    public async Task The_line_says_no_mail_was_sent()
    {
        var log = new CapturingLogger<ConsoleVerificationMailer>();

        await new ConsoleVerificationMailer(log).SendAsync("ana.ferreira@example.pt", "314159", default);

        log.Lines.Single().Should().Contain("No mail was sent");
    }
}
