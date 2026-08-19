using System.Reflection;

namespace vos.Tests.Shared;

// The property names a compute handler writes, read off the handler rather than restated in a test.
//
// A handler publishes each output as a public string constant and its inputs as InputProperties, so the
// constants are exactly the outputs — and an output added later joins this set by having been declared,
// where a list in a test would silently stay one short.
public static class DeclaredOutputs
{
    public static IReadOnlySet<string> Of<THandler>()
    {
        var outputs = typeof(THandler)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        // Every assertion over this set is that something is absent from it, so an empty answer would pass
        // each one without checking anything. A handler that stops publishing its outputs as constants has
        // to be noticed here rather than in the silence afterwards.
        if (outputs.Count == 0)
            throw new InvalidOperationException(
                $"{typeof(THandler).Name} declares no output as a public string constant, so there is "
                + "nothing to check against.");

        return outputs;
    }
}
