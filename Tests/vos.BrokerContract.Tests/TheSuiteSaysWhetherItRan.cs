using Xunit;

namespace vos.BrokerContract.Tests;

/// <summary>
/// The cases in <c>AgainstTheEngine</c> compile only when an engine is staged, so a checkout without
/// one builds and tests like any other — and would otherwise report a green run over nothing. This
/// says which of the two happened, in the place a reader looks: the test results.
///
/// It is not the guard against a build that runs the empty variant. That is <c>VosRequireEngine</c>
/// in the project file, which fails the build rather than the suite.
/// </summary>
public class TheSuiteSaysWhetherItRan
{
#if VOS_ENGINE_IS_STAGED
    [Fact]
    public void The_cases_that_need_the_engine_were_compiled()
    {
        var compiled = typeof(TheSuiteSaysWhetherItRan).Assembly.GetTypes()
            .Count(type => type.Namespace == "vos.BrokerContract.Tests.AgainstTheEngine");

        Assert.True(compiled > 0,
            "an engine is staged and this assembly holds no case that uses it, so the suite is "
            + "reporting a pass over nothing");
    }
#else
    [Fact(Skip = "No engine is staged, so nothing here ran. Stage one with ci/stage-the-engine.sh, "
                 + "which builds it from a platform checkout beside this one.")]
    public void The_cases_that_need_the_engine_were_compiled()
    {
    }
#endif
}
