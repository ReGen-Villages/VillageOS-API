using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>Where a verification code is handed to be delivered, and what the message says. A submission
/// is accepted only from somebody who answered one, so a deployment that cannot send is a deployment
/// nobody can submit to.</summary>
public class MailSettingsTests
{
    private static IConfiguration Configured(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static readonly MailSettings Relay =
        new("smtp.example.test", 587, "intake@example.test", null, null);

    [Fact]
    public void The_server_and_the_address_it_sends_from_are_read_off_the_command_line()
    {
        var parsed = MailSettings.Parse(
            ["--mailHost=smtp.example.test", "--mailFrom=intake@example.test", "--mailPort=2525"],
            Configured());

        parsed.Should().Be(new MailSettings("smtp.example.test", 2525, "intake@example.test", null, null));
    }

    [Fact]
    public void A_port_nobody_named_is_the_one_mail_is_submitted_on()
    {
        MailSettings.Parse(["--mailHost=smtp.example.test", "--mailFrom=intake@example.test"], Configured())!
            .Port.Should().Be(MailSettings.SubmissionPort);
    }

    // The service prints what is missing and stops on null. Starting without these would answer every
    // verification with a failure, and the first anybody would know of it is a submitter unable to submit.
    [Theory]
    [InlineData("--mailFrom=intake@example.test")]
    [InlineData("--mailHost=smtp.example.test")]
    [InlineData()]
    public void Settings_that_cannot_send_are_no_settings_at_all(params string[] arguments)
    {
        MailSettings.Parse(arguments, Configured()).Should().BeNull();
    }

    // The command line is visible to every process on the host and to anything recording how a service was
    // started, which is why this one setting is read from configuration alone.
    [Fact]
    public void The_password_is_read_from_configuration_and_never_from_the_command_line()
    {
        var parsed = MailSettings.Parse(
            ["--mailHost=smtp.example.test", "--mailFrom=intake@example.test", "--mailUser=intake",
             "--mailPassword=typed-on-the-command-line"],
            Configured(("MailPassword", "held-in-configuration")));

        parsed!.Password.Should().Be("held-in-configuration");
    }

    // The usage is the whole of what somebody sees when the service will not start, so a flag missing
    // from it leaves them reading a refusal that does not say what to do about it.
    [Theory]
    [InlineData("--mailHost")]
    [InlineData("--mailPort")]
    [InlineData("--mailFrom")]
    [InlineData("--mailUser")]
    [InlineData("--mailPassword")]
    public void The_usage_names_every_setting_that_can_be_given(string flag)
    {
        MailSettings.UsageMessage.Should().Contain(flag);
    }

    [Fact]
    public void A_server_that_asks_for_no_account_is_relayed_to_without_one()
    {
        SmtpVerificationMailer.CredentialFor(Relay).Should().BeNull();
    }

    [Fact]
    public void A_server_that_asks_for_an_account_is_relayed_to_under_it()
    {
        var credential = SmtpVerificationMailer.CredentialFor(Relay with { Username = "intake", Password = "secret" });

        credential!.UserName.Should().Be("intake");
        credential.Password.Should().Be("secret");
    }

    // Whoever reads the mailbox may not be whoever filled the form in — a verification proves only that
    // somebody reads it — so the message says what the code is for and nothing about the submission.
    [Fact]
    public void The_message_carries_the_code_and_nothing_of_what_is_being_submitted()
    {
        using var message = SmtpVerificationMailer.MessageFor(Relay, "ana.ferreira@example.pt", "314159");

        message.To.Single().Address.Should().Be("ana.ferreira@example.pt");
        message.From!.Address.Should().Be("intake@example.test");
        message.Subject.Should().NotBeNullOrWhiteSpace();
        message.Body.Should().Contain("314159")
            .And.Contain($"{(int)AddressVerification.ValidFor.TotalMinutes} minutes",
                "somebody who cannot act on it now needs to know whether it will still work later");
        message.Body.Should().NotContain("Willow Bend").And.NotContain("submissionId");
    }

    // Somebody who never asked has to be able to tell that nothing happened in their name.
    [Fact]
    public void The_message_says_what_to_do_where_nobody_asked_for_it()
    {
        using var message = SmtpVerificationMailer.MessageFor(Relay, "ana.ferreira@example.pt", "314159");

        message.Body.Should().Contain("did not ask");
    }
}
