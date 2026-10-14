using vos.BrokerContract.Tests.AgainstTheEngine;
using Xunit;

namespace vos.BrokerContract.Tests;

// Only the platform's build stages an engine, so a case this repository's build left out would go
// unnoticed here when a service changed a constructor the case calls.
public class TheCasesAgainstTheEngineAlwaysCompile
{
    [Fact]
    public void The_cases_that_need_the_engine_are_compiled_whether_or_not_one_is_staged()
    {
        var compiled = typeof(TheCasesAgainstTheEngineAlwaysCompile).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Count(method => method.IsDefined(typeof(FactAgainstTheEngineAttribute), inherit: false));

        Assert.True(compiled > 0, "this assembly holds no case that runs against the engine");
    }
}
