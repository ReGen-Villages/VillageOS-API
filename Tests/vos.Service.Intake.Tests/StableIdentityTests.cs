using FluentAssertions;
using vos.Service.Intake.Helpers;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>The expected identifiers are RFC 4122 version 5 UUIDs computed outside this codebase, so a
/// derivation done in another language for the same submission agrees with this one.</summary>
public class StableIdentityTests
{
    [Fact]
    public void A_submission_derives_the_identifier_the_standard_gives()
    {
        StableIdentity.Derive("willow-bend-2026-08", "site")
            .Should().Be(new Guid("57ab6c31-9611-5cb5-b31b-5d5bb9d8c2a6"));
    }

    [Fact]
    public void A_predicate_derives_the_identifier_the_standard_gives()
    {
        StableIdentity.DerivePredicate("has")
            .Should().Be(new Guid("5cc88a57-33bc-5a8b-bce8-2a6054882311"));
    }

    [Fact]
    public void Two_roles_within_one_submission_are_different_things()
    {
        StableIdentity.Derive("willow-bend-2026-08", "site")
            .Should().NotBe(StableIdentity.Derive("willow-bend-2026-08", "study"));
    }
}
