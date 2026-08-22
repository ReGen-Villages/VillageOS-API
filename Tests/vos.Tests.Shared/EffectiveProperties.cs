namespace vos.Tests.Shared;

// A study's effective properties in the shape the broker answers with — each value inside a { "Value": … }
// envelope — for a test that cares which names are present rather than what they are worth.
//
// Every value is one, which leaves each balance's arithmetic defined: no input a calculator divides by is
// zero, so a study built this way computes whenever the names are the ones the calculator reads.
public static class EffectiveProperties
{
    public static string Carrying(IEnumerable<string> names) =>
        "{" + string.Join(",", names.Select(name => $"\"{name}\": {{ \"Value\": 1 }}")) + "}";
}
