using vos.BrokerContract.Tests.AgainstTheEngine;
using Xunit;

namespace vos.BrokerContract.Tests;

public class TheCasesSkipOnlyWithoutAnEngine
{
#if VOS_ENGINE_IS_STAGED
    [Fact]
    public void With_an_engine_staged_a_case_runs() =>
        Assert.Null(new FactAgainstTheEngineAttribute().Skip);
#else
    [Fact]
    public void Without_an_engine_a_case_is_skipped_and_says_why() =>
        Assert.StartsWith("No engine is staged", new FactAgainstTheEngineAttribute().Skip);

    [Fact]
    public async Task Without_an_engine_the_fixture_refuses_a_case_that_is_not_marked_to_skip()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new TheEngine().InitializeAsync());

        Assert.Contains("[FactAgainstTheEngine]", refused.Message);
    }
#endif
}
