namespace vos.Taproot;

internal static class OperatorMessage
{
    // .NET keeps the reason a call failed in the wrapped exception: an HttpRequestException saying
    // "see inner exception" carries the refused certificate, the refused connection or the timeout
    // underneath it. Reporting only the outer message tells the operator to consult something they
    // were never shown.
    public static string For(Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException is not null)
            innermost = innermost.InnerException;

        return exception.Message.Contains(innermost.Message, StringComparison.Ordinal)
            ? exception.Message
            : $"{exception.Message} ({innermost.Message})";
    }
}
