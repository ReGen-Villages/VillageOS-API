namespace vos.Taproot;

internal static class OperatorMessage
{
    // .NET keeps the reason in the wrapped exception, so an HttpRequestException that says "see
    // inner exception" is pointing the operator at something the CLI never shows them.
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
