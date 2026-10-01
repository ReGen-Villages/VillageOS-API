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
    internal sealed record Wait(int Line, string Call, IReadOnlyList<string> Arguments, bool AlwaysTakesAClock);

    private static readonly Regex TakesAClock = new(
        @"\bTask\.Delay\s*\(|\bThread\.Sleep\s*\(|\bnew\s+CancellationTokenSource\s*\(|\.CancelAfter\s*\(");

    // These also take a cancellation token, a condition, a list of tasks, or nothing, and none of
    // those is a clock.
    private static readonly Regex MayTakeAClock = new(@"\.Wait(?:Async|One|All|Any)?\s*\(|\bSpinUntil\s*\(");

    private static readonly Regex WrittenAsADuration = new(@"^(?:\w+:\s*)?\d|TimeSpan\.From");

    private static readonly Regex LineComment = new(@"(?<!:)//.*$", RegexOptions.Multiline);

    private static readonly string[] SharedCeilingOrNoClock =
        ["Settle.Ceiling", "Timeout.Infinite", "Timeout.InfiniteTimeSpan"];

    internal static IReadOnlyList<Wait> In(string source)
    {
        var code = LineComment.Replace(source, "");

        return WaitsMatching(TakesAClock, code, alwaysTakesAClock: true)
            .Concat(WaitsMatching(MayTakeAClock, code, alwaysTakesAClock: false))
            .OrderBy(wait => wait.Line)
            .ToList();
    }

    // A variable passed to a call that may take a clock is let through, because the text does not
    // say whether it is a duration or a cancellation token.
    internal static bool KeepsItsOwnClock(Wait wait) =>
        wait.AlwaysTakesAClock
            ? wait.Arguments.Count > 0 && !SharedCeilingOrNoClock.Contains(wait.Arguments[0])
            : wait.Arguments.Any(WrittenAsADuration.IsMatch);

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
                Arguments: ArgumentsIn(code[(opened + 1)..closed]),
                alwaysTakesAClock);
        }
    }

    private static List<string> ArgumentsIn(string betweenTheBrackets)
    {
        var arguments = new List<string>();
        var depth = 0;
        var from = 0;

        for (var at = 0; at < betweenTheBrackets.Length; at++)
        {
            if (betweenTheBrackets[at] is '(' or '[' or '{') depth++;
            else if (betweenTheBrackets[at] is ')' or ']' or '}') depth--;
            else if (betweenTheBrackets[at] == ',' && depth == 0)
            {
                arguments.Add(betweenTheBrackets[from..at].Trim());
                from = at + 1;
            }
        }

        var last = betweenTheBrackets[from..].Trim();
        if (last.Length > 0) arguments.Add(last);
        return arguments;
    }
}
