namespace vos.ContinuousIntegration.Tests;

internal static class SourceCalls
{
    // Where the call whose bracket opens at the given position closes, or -1 when the source ends
    // first. Found by balancing, because a call holds nested calls of its own.
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
