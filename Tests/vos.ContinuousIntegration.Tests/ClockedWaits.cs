using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace vos.ContinuousIntegration.Tests;

// Reads the waits a test file puts a clock on, from its source.
//
// Each call is judged by the clock written into it, because a file can hold a wait on the shared
// ceiling beside one that gives up after five seconds.
internal static class ClockedWaits
{
    internal sealed record Wait(int Line, string Call, string Clock, bool AlwaysTakesAClock);

    private static readonly Regex TakesAClock = new(
        @"\bTask\.Delay\s*\(|\bThread\.Sleep\s*\(|\bnew\s+CancellationTokenSource\s*\(|\.CancelAfter\s*\(");

    // Wait and WaitAsync also take a cancellation token, or nothing, and neither is a clock.
    private static readonly Regex MayTakeAClock = new(@"\.Wait(?:Async)?\s*\(");

    private static readonly Regex WrittenAsADuration = new(@"^\d|TimeSpan\.From");

    private static readonly Regex LineComment = new(@"(?<!:)//.*$", RegexOptions.Multiline);

    private static readonly string[] SharedCeilingOrNoClock =
        ["", "Settle.Ceiling", "Timeout.Infinite", "Timeout.InfiniteTimeSpan"];

    internal static IReadOnlyList<Wait> In(string source)
    {
        var code = LineComment.Replace(source, "");

        return WaitsMatching(TakesAClock, code, alwaysTakesAClock: true)
            .Concat(WaitsMatching(MayTakeAClock, code, alwaysTakesAClock: false))
            .OrderBy(wait => wait.Line)
            .ToList();
    }

    // A variable passed to Wait or WaitAsync is let through, because the text does not say whether
    // it is a duration or a cancellation token.
    internal static bool KeepsItsOwnClock(Wait wait) =>
        wait.AlwaysTakesAClock
            ? !SharedCeilingOrNoClock.Contains(wait.Clock)
            : WrittenAsADuration.IsMatch(wait.Clock);

    private static IEnumerable<Wait> WaitsMatching(Regex opening, string code, bool alwaysTakesAClock)
    {
        foreach (Match match in opening.Matches(code))
        {
            var opened = match.Index + match.Length - 1;
            var closed = SourceCalls.ClosingBracket(code, opened);
            if (closed < 0) continue;

            yield return new Wait(
                Line: code.AsSpan(0, match.Index).Count('\n') + 1,
                Call: code[match.Index..(closed + 1)].TrimStart('.'),
                Clock: FirstArgument(code[(opened + 1)..closed]),
                alwaysTakesAClock);
        }
    }

    private static string FirstArgument(string arguments)
    {
        var depth = 0;
        for (var at = 0; at < arguments.Length; at++)
        {
            if (arguments[at] is '(' or '[' or '{') depth++;
            else if (arguments[at] is ')' or ']' or '}') depth--;
            else if (arguments[at] == ',' && depth == 0) return arguments[..at].Trim();
        }

        return arguments.Trim();
    }
}
