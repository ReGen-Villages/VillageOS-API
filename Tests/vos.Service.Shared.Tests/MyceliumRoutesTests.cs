using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// The routes every service speaks through. A rename here is one edit; these say what the edit has to
// keep true.
public class MyceliumRoutesTests
{
    private static readonly Guid Thing = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void A_thing_s_resolved_properties_are_read_from_its_properties_route()
    {
        MyceliumRoutes.ThingProperties(Thing).Should().Be($"/api/things/{Thing}/properties");
    }

    [Fact]
    public void A_fact_is_posted_under_the_property_it_asserts()
    {
        MyceliumRoutes.ThingPropertyFacts(Thing, "peopleFed")
            .Should().Be($"/api/things/{Thing}/properties/peopleFed/facts");
    }

    // Property names come from the model, so nothing stops a planner declaring one with a space or a
    // slash in it. Unescaped, the slash would make the name look like two path segments and the request
    // would reach a route that does not exist.
    [Fact]
    public void A_property_name_that_would_change_the_path_is_escaped_into_one_segment()
    {
        MyceliumRoutes.ThingPropertyFacts(Thing, "people fed/year")
            .Should().Be($"/api/things/{Thing}/properties/people%20fed%2Fyear/facts");
    }
}
