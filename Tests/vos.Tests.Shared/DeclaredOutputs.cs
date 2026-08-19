using System.Reflection;

namespace vos.Tests.Shared;

// The property names a compute handler writes, read off the handler rather than restated in a test.
//
// A handler publishes each output as a public string constant and its inputs as InputProperties, so the
// constants are exactly the outputs — and an output added later joins this set by having been declared,
// where a list in a test would silently stay one short.
public static class DeclaredOutputs
{
    public static IReadOnlySet<string> Of<THandler>() =>
        typeof(THandler)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
}
