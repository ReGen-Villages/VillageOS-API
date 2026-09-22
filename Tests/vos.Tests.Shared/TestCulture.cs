using System.Globalization;

namespace vos.Tests.Shared;

// Runs a body under a chosen regional format, so a test's result does not depend on the machine's
// settings. Two uses, and they are not the same thing:
//
//   CommaDecimal — prove that reading a value does NOT depend on the regional format. Under nl-NL
//   "30.5" parses as 305 unless the invariant culture is used, because the dot reads as a thousands
//   separator and no error is raised. Without forcing the culture such a test passes on an en-US
//   build agent and only fails on a machine already set to a comma-decimal region.
//
//   Display — pin output that IS meant to follow the operator's region. Asserting "4.0%" is
//   asserting a rendering, so the test must name the culture it expects rather than inherit one.
//   en-US, not the invariant culture: invariant's percent pattern puts a space before the sign
//   ("4.0 %"), which is not the rendering these assertions were written against.
public static class TestCulture
{
    public static readonly CultureInfo CommaDecimal = new("nl-NL");

    public static readonly CultureInfo Display = new("en-US");

    public static T In<T>(CultureInfo culture, Func<T> body)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    public static async Task InAsync(CultureInfo culture, Func<Task> body)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    public static async Task<T> InAsync<T>(CultureInfo culture, Func<Task<T>> body)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
