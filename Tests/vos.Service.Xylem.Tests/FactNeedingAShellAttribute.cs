using Xunit;

namespace vos.Service.Xylem.Tests;

public sealed class FactNeedingAShellAttribute : FactAttribute
{
    public FactNeedingAShellAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "The child process this test starts is /bin/sh, which Windows does not have.";
    }
}
