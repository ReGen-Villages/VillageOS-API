using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public sealed class FactAgainstTheEngineAttribute : FactAttribute
{
    public FactAgainstTheEngineAttribute()
    {
#if !VOS_ENGINE_IS_STAGED
        Skip = "No engine is staged, so this case did not run. Stage one with ci/stage-the-engine.sh, "
               + "which builds it from a platform checkout beside this one.";
#endif
    }
}
