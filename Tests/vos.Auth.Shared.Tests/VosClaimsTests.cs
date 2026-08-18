using FluentAssertions;
using vos.Auth.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

public class VosClaimsTests
{
    [Fact]
    public void ModelId_matches_the_claim_mycelium_mints()
    {
        VosClaims.ModelId.Should().Be("vos:model_id");
    }
}
