using System;
using System.Collections.Generic;
using System.Linq;

namespace vos.ContinuousIntegration.Tests;

// Reads what a service entry point binds, from its source.
//
// The question is what an address reaches, not how someone chose to spell it: a service is free to
// write its loopback binding as an interpolated string, a concatenation, or a Kestrel listen call, and
// all three are correct. Judging one spelling refuses the other two and — worse — says nothing about a
// second address alongside it that reaches the whole network.
internal static class ServiceBindings
{
    // Calls that decide what a managed service listens on.
    private static readonly string[] BindingCalls = ["UseUrls(", "ListenLocalhost(", "ListenAnyIP(", ".Listen("];

    // Addresses that reach past the machine. Kestrel treats * and + as every
    // interface exactly as 0.0.0.0 does, which is what made a wildcard binding pass unnoticed.
    // [::] does not match the loopback [::1], whose digit sits inside the brackets.
    private static readonly string[] BeyondLoopback =
        ["0.0.0.0", "[::]", "ListenAnyIP", "IPAddress.Any", "://*:", "://+:"];

    private static readonly string[] LoopbackAddresses = ["localhost", "127.0.0.1", "[::1]", "IPAddress.Loopback"];

    // The text of every binding call the source makes, each from the call to its closing
    // bracket, so an address is judged with the call that binds it rather than on its own.
    internal static IReadOnlyList<string> BindingCallsIn(string source)
    {
        var calls = new List<string>();

        foreach (var call in BindingCalls)
        {
            var at = source.IndexOf(call, StringComparison.Ordinal);
            while (at >= 0)
            {
                var opened = at + call.Length - 1;
                var closed = MatchingBracket(source, opened);
                if (closed > opened) calls.Add(source[at..(closed + 1)]);
                at = source.IndexOf(call, at + call.Length, StringComparison.Ordinal);
            }
        }

        return calls;
    }

    internal static bool IsLoopback(string bindingCall) =>
        bindingCall.StartsWith("ListenLocalhost(", StringComparison.Ordinal)
        || (LoopbackAddresses.Any(address => bindingCall.Contains(address, StringComparison.Ordinal))
            && !ReachesBeyondLoopback(bindingCall));

    // Whether the text names an address reaching past the machine. Asked of a whole entry point
    // as well as of one call, so a service written in a language this cannot parse is still refused a
    // binding that would put it on the network.
    internal static bool ReachesBeyondLoopback(string text) =>
        BeyondLoopback.Any(address => text.Contains(address, StringComparison.Ordinal));

    private static int MatchingBracket(string source, int opened)
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
