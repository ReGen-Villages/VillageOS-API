using FluentAssertions;
using Xunit;

namespace vos.Service.Auth.Tests;

public class VosClaimsTests
{
    [Fact]
    public void ModelId_matches_the_claim_mycelium_mints()
    {
        VosClaims.ModelId.Should().Be("vos:model_id");
    }

    [Fact]
    public void TokenType_matches_the_claim_mycelium_mints()
    {
        VosClaims.TokenType.Should().Be("vos:token_type");
    }
}
