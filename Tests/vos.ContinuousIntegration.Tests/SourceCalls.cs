namespace vos.ContinuousIntegration.Tests;

internal static class SourceCalls
{
    // Found by balancing, because a call holds nested calls of its own. Negative when the source
    // ends before the call closes.
    internal static int ClosingBracket(string source, int opened)
    {
        var depth = 0;
        for (var at = opened; at < source.Length; at++)
        {
            if (source[at] == '(') depth++;
            else if (source[at] == ')' && --depth == 0) return at;
        }

        return -1;
    }
}
