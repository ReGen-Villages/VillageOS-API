using FluentAssertions;
using vos.Service.Delta.Models;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Delta.Tests.Models;

public class EndpointTemplateTreeTests
{
    private readonly List<SnapshotThing> _things = new();
    private readonly List<SnapshotRelationship> _relationships = new();
    private readonly Guid _is;
    private readonly Guid _binds;

    public EndpointTemplateTreeTests()
    {
        _is = Thing("is");
        _binds = Thing("binds");
        Thing("endpoint role", id: EndpointTemplateTree.EndpointRoleId);
    }

    [Fact]
    public void A_registration_naming_no_template_descends_from_the_bound_root()
    {
        var root = Template("Endpoint");
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        var (template, refusal) = Locate(null);

        refusal.Should().BeNull();
        template.Should().Be(new LocatedTemplate(root, "Endpoint", _is));
    }

    [Fact]
    public void A_template_two_levels_beneath_the_root_is_found()
    {
        var root = Template("Endpoint");
        var middle = Template("EsriEndpoint");
        var leaf = Template("EsriFeatureEndpoint");
        Relate(middle, _is, root);
        Relate(leaf, _is, middle);
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        Locate("EsriFeatureEndpoint").Template!.Id.Should().Be(leaf);
    }

    [Fact]
    public void A_template_name_differing_only_in_case_is_not_found()
    {
        var root = Template("Endpoint");
        var child = Template("EsriEndpoint");
        Relate(child, _is, root);
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        var (template, refusal) = Locate("esriendpoint");

        template.Should().BeNull("Thing names are matched exactly, case included");
        refusal.Should().NotBeNull();
    }

    [Fact]
    public void A_Thing_named_Is_beside_is_is_another_Thing_and_leaves_registering_alone()
    {
        var root = Template("Endpoint");
        var child = Template("EsriEndpoint");
        Relate(child, _is, root);
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);
        Thing("Is");

        Locate("EsriEndpoint").Template!.Id.Should().Be(child, "only a Thing named exactly `is` is the is predicate");
    }

    [Fact]
    public void A_cycle_in_the_is_edges_ends_in_a_refusal_rather_than_a_hang()
    {
        var root = Template("Endpoint");
        var first = Template("First");
        var second = Template("Second");
        Relate(first, _is, second);
        Relate(second, _is, first);
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        Locate("First").Refusal!.StatusCode.Should().Be(400);
    }

    [Fact]
    public void A_role_binding_two_things_is_refused_naming_both()
    {
        var first = Template("Endpoint");
        var second = Template("OtherEndpoint");
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, first);
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, second);

        var refusal = Locate(null).Refusal!;

        refusal.StatusCode.Should().Be(409);
        refusal.Reason.Should().Contain(first.ToString()).And.Contain(second.ToString());
    }

    [Fact]
    public void A_relationship_from_the_role_that_is_not_binds_binds_nothing()
    {
        var configuration = Template("Configuration");
        Relate(EndpointTemplateTree.EndpointRoleId, _is, configuration);

        Locate(null).Refusal!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void A_model_with_no_is_predicate_is_refused()
    {
        _things.RemoveAll(thing => thing.Id == _is);
        var root = Template("Endpoint");
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        var refusal = Locate(null).Refusal!;

        refusal.StatusCode.Should().Be(500);
        refusal.Reason.Should().Contain("'is'");
    }

    [Fact]
    public void A_model_with_two_is_predicates_is_refused()
    {
        Thing("is");
        var root = Template("Endpoint");
        Relate(EndpointTemplateTree.EndpointRoleId, _binds, root);

        Locate(null).Refusal!.StatusCode.Should().Be(500);
    }

    private (LocatedTemplate? Template, TemplateRefusal? Refusal) Locate(string? templateName) =>
        EndpointTemplateTree.Locate(new SnapshotDocument(0, _things, _relationships), templateName);

    private Guid Template(string name) => Thing(name, isArchetype: true);

    private Guid Thing(string name, bool isArchetype = false, Guid? id = null)
    {
        var thing = new SnapshotThing(id ?? Guid.NewGuid(), name, isArchetype, new(), null, [], null);
        _things.Add(thing);
        return thing.Id;
    }

    private void Relate(Guid subject, Guid predicate, Guid target) =>
        _relationships.Add(new SnapshotRelationship(Guid.NewGuid(), null, subject, predicate, target, new(), null, []));
}
