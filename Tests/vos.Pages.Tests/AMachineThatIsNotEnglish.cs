using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace vos.Pages.Tests;

// Runs every test in this assembly as a machine set to Swedish would, where Å and Ö sort after Z. The
// engine takes its default language from the process environment the first time it is asked, and a
// value set through .NET alone never reaches it, so it is set in the C library before any engine exists.
internal static class AMachineThatIsNotEnglish
{
    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int SetInTheCLibrary(string name, string value, int overwrite);

    [ModuleInitializer]
    internal static void Set()
    {
        if (SetInTheCLibrary("LC_ALL", "sv_SE.UTF-8", overwrite: 1) != 0)
            throw new InvalidOperationException("The machine's language could not be set for the tests.");
    }
}
