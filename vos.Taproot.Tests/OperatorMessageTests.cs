using System.Net.Sockets;
using FluentAssertions;
using vos.Taproot;
using Xunit;

namespace vos.Taproot.Tests;

public class OperatorMessageTests
{
    [Fact]
    public void For_ExceptionWithNoInnerException_IsTheMessageUnchanged()
    {
        var exception = new InvalidOperationException("Nothing is listening on that address");

        OperatorMessage.For(exception).Should().Be("Nothing is listening on that address");
    }

    [Fact]
    public void For_WrappedException_AddsTheReasonTheOuterMessageWithholds()
    {
        var exception = new HttpRequestException(
            "The SSL connection could not be established, see inner exception.",
            new Exception("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot"));

        OperatorMessage.For(exception).Should().Be(
            "The SSL connection could not be established, see inner exception. "
            + "(The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot)");
    }

    [Fact]
    public void For_ChainSeveralDeep_ReportsTheInnermostReason()
    {
        var exception = new HttpRequestException(
            "outer",
            new InvalidOperationException("middle", new Exception("the reason")));

        OperatorMessage.For(exception).Should().Be("outer (the reason)");
    }

    [Fact]
    public void For_InnerExceptionRepeatingTheOuterMessage_SaysItOnce()
    {
        var exception = new InvalidOperationException("Connection refused", new Exception("Connection refused"));

        OperatorMessage.For(exception).Should().Be("Connection refused");
    }

    // The real chain for an unreachable host: the outer message is the inner one plus the address.
    // Appending it again would read "Connection refused (localhost:7243) (Connection refused)".
    [Fact]
    public void For_OuterMessageAlreadyContainingTheReason_DoesNotRepeatIt()
    {
        var exception = new HttpRequestException(
            "Connection refused (localhost:7243)",
            new SocketException((int)SocketError.ConnectionRefused));

        OperatorMessage.For(exception).Should().Be("Connection refused (localhost:7243)");
    }
}
