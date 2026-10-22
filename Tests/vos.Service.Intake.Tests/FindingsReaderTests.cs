using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

// What a submitter's page is drawn from, and — the half that matters more — what it is not. Read over a
// snapshot in the shape the broker writes one, because the answer carries that shape through untouched.
public class FindingsReaderTests
{
    private static readonly Guid ThisContact = BrokerSnapshot.ContactOf(WillowBend.SubmissionId);
    private static readonly Guid ThisSite = BrokerSnapshot.SiteOf(WillowBend.SubmissionId);
    private static readonly Guid OtherSite = BrokerSnapshot.SiteOf(BrokerSnapshot.OtherSubmissionId);

    [Fact]
    public void It_reads_the_page_the_model_binds_and_the_address_the_submission_names()
    {
        using var model = BrokerSnapshot.WithTwoSubmissions().Parsed();

        var declared = FindingsReader.ReadDeclarations(model.RootElement, ThisContact);

        declared.Specification.Should().Contain("Site submission");
        declared.PersonalDetailArchetype.Should().Be(BrokerSnapshot.ContactArchetype);
        declared.ContactAddress.Should().Be(BrokerSnapshot.AddressOn("Willow Bend"));
    }

    // A deployment that was never seeded for this, not a request anybody can correct.
    [Theory]
    [InlineData("submitterFindingsDashboard")]
    [InlineData("personalDetail")]
    public void It_refuses_a_model_binding_either_role_to_nothing(string unbound)
    {
        var role = new[] { FindingsReader.PageRole, FindingsReader.PersonalDetailRole }.Single(one => one.Word == unbound);
        using var model = BrokerSnapshot.WithTwoSubmissions().Unbound(role).Parsed();

        var refusal = Assert.Throws<ModelNotSeededError>(
            () => FindingsReader.ReadDeclarations(model.RootElement, ThisContact));

        refusal.Message.Should().Contain(unbound);
    }

    // Which of them was meant is not this service's to guess, and guessing would draw one submitter a page
    // somebody authored for a different reader.
    [Fact]
    public void It_refuses_a_model_binding_the_page_role_to_two_pages()
    {
        var secondPage = Guid.NewGuid();
        using var model = BrokerSnapshot.WithTwoSubmissions()
            .Thing(secondPage, "A second page", properties: new Dictionary<string, object>
            {
                [FindingsReader.SpecificationProperty] = new { typeInfo = "vos.String", value = "{}" },
            })
            .Bound(FindingsReader.PageRole, secondPage)
            .Parsed();

        Assert.Throws<ModelNotSeededError>(() => FindingsReader.ReadDeclarations(model.RootElement, ThisContact));
    }

    // The roles are what the reader asks for, so a model calling its page and its contact archetype anything
    // still answers — and the binding is read off the role Thing's own relationship, never off a name.
    [Fact]
    public void It_reads_a_page_and_an_archetype_of_other_names_through_their_roles()
    {
        var page = Guid.NewGuid();
        var people = Guid.NewGuid();
        using var model = BrokerSnapshot.WithTwoSubmissions()
            .Unbound(FindingsReader.PageRole).Unbound(FindingsReader.PersonalDetailRole)
            .Thing(page, "What the land says", properties: new Dictionary<string, object>
            {
                [FindingsReader.SpecificationProperty] = new { typeInfo = "vos.String", value = "{}" },
            })
            .Thing(people, "People", isArchetype: true)
            .Bound(FindingsReader.PageRole, page)
            .Bound(FindingsReader.PersonalDetailRole, people)
            .Parsed();

        var declared = FindingsReader.ReadDeclarations(model.RootElement, ThisContact);

        declared.Specification.Should().Be("{}");
        declared.PersonalDetailArchetype.Should().Be(people);
    }

    // The role Things by identifier with their bindings followed, the one contact, and only the `binds`
    // relationships: the contact's own relationships would say what its site holds, to a read that answers
    // nobody.
    [Fact]
    public void The_declaration_read_asks_for_the_roles_and_the_one_contact_and_nothing_else()
    {
        var selector = FindingsReader.DeclarationSelector(ThisContact);

        selector.Ids.Should().Equal(ThisContact, FindingsReader.PageRole.Id, FindingsReader.PersonalDetailRole.Id);
        selector.Names.Should().Equal(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings);
        selector.Relationships.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Predicate = RoleBindings.BindsPredicateName, Direction = "outgoing" });
        selector.IncludeIsAncestors.Should().BeFalse();
        selector.MarkedArchetypes.Should().BeNull();
        selector.Types.Should().BeNull();
        selector.All.Should().BeFalse();
    }

    // Every predicate the page walks, in the direction it walks it — and the `is` relationship, without which no
    // Thing in the answer can be told what it is.
    [Fact]
    public void The_findings_read_follows_the_edges_the_page_walks()
    {
        var selector = FindingsReader.FindingsSelector(ThisSite, BrokerSnapshot.PageSpecification);

        selector.Ids.Should().Equal(ThisSite);
        selector.Names.Should().BeEquivalentTo(["has", "obtainedBy", "studies", "is"]);
        selector.Traverse!.Should().BeEquivalentTo(
        [
            new { Predicate = "has", Direction = "outgoing" },
            new { Predicate = "obtainedBy", Direction = "outgoing" },
            new { Predicate = "studies", Direction = "incoming" },
        ], options => options.ExcludingMissingMembers());
    }

    [Fact]
    public void A_page_offering_levers_also_reads_the_names_its_inputs_are_given_and_the_archetypes_they_name()
    {
        var offeringLevers = BrokerSnapshot.PageSpecification.Replace(
            "\"reads\": \"the site makes what it uses\"",
            "\"reads\": \"the site makes what it uses\", \"levers\": {\"raise\": \"More {term}\", \"lower\": \"Less {term}\"}");

        var selector = FindingsReader.FindingsSelector(ThisSite, offeringLevers);

        selector.BoundTypes.Should().Equal(FindingsReader.PropertyNameRole.Id);
        selector.Ids.Should().Equal(ThisSite, FindingsReader.PropertyNameRole.Id);
        selector.Names.Should().Contain(FindingsReader.NamesPropertyOfPredicate)
            .And.Contain(RoleBindings.BindsPredicateName);
        selector.Traverse![^2].Should().BeEquivalentTo(
            new { Predicate = FindingsReader.NamesPropertyOfPredicate, Direction = "outgoing", Depth = 1 });
        selector.Traverse![^1].Should().Be(RoleBindings.FollowBindings);
    }

    [Fact]
    public void A_page_offering_no_levers_reads_no_names()
    {
        var selector = FindingsReader.FindingsSelector(ThisSite, BrokerSnapshot.PageSpecification);

        selector.BoundTypes.Should().BeNull();
        selector.Ids.Should().Equal(ThisSite);
        selector.Names.Should().NotContain(FindingsReader.NamesPropertyOfPredicate)
            .And.NotContain(RoleBindings.BindsPredicateName);
    }

    // A selector naming the archetypes would answer with every Thing of that kind, which in a staging
    // model is every other submitter's land.
    [Fact]
    public void The_findings_read_names_no_archetype_to_be_answered_with_the_members_of()
    {
        var selector = FindingsReader.FindingsSelector(ThisSite, BrokerSnapshot.PageSpecification);

        selector.Types.Should().BeNull();
        selector.All.Should().BeFalse();
    }

    [Fact]
    public void It_answers_with_neither_the_archetype_carrying_personal_details_nor_its_members()
    {
        using var model = BrokerSnapshot.WithTwoSubmissions().Parsed();

        var answered = FindingsReader.ThingsToAnswerWith(model.RootElement, BrokerSnapshot.ContactArchetype);

        Identifiers(answered).Should().NotContain(ThisContact);
        Identifiers(answered).Should().NotContain(BrokerSnapshot.ContactArchetype);
        Identifiers(answered).Should().Contain(ThisSite);
    }

    // A relationship to a Thing that was withheld names an identifier the answer does not carry, and says the
    // site relates to something the reader is not shown.
    [Fact]
    public void It_answers_with_no_edge_naming_a_withheld_thing()
    {
        using var model = BrokerSnapshot.WithTwoSubmissions().Parsed();
        var things = FindingsReader.ThingsToAnswerWith(model.RootElement, BrokerSnapshot.ContactArchetype);

        var edges = FindingsReader.RelationshipsToAnswerWith(model.RootElement, things);

        var answered = Identifiers(things).ToHashSet();
        edges.Should().OnlyContain(edge =>
            answered.Contains(Guid.Parse(edge.GetProperty("SubjectId").GetString()!))
            && answered.Contains(Guid.Parse(edge.GetProperty("TargetId").GetString()!)));
    }

    // A verdict row is drawn only for a Thing in one of the states its specification names, and the target it reads
    // comes off the range that judged it — so this is exactly the set the page will ask about, and no more.
    [Fact]
    public void Only_a_thing_holding_a_state_has_its_ranges_asked_for()
    {
        using var whole = BrokerSnapshot.WithTwoSubmissions().Parsed();
        using var model = OnlyReaching(whole.RootElement, ThisSite);
        var things = FindingsReader.ThingsToAnswerWith(model.RootElement, BrokerSnapshot.ContactArchetype);

        FindingsReader.JudgedThings(things).Should()
            .Equal(BrokerSnapshot.StudyOf(WillowBend.SubmissionId));
    }

    // What says whether a figure was stated or measured is the write kind the broker wrote beside it. A
    // service that read a snapshot into a shape of its own would drop it, and every provenance line on the
    // page would read as unrecorded.
    [Fact]
    public void A_value_travels_in_the_envelope_the_broker_wrote_it_in()
    {
        using var model = BrokerSnapshot.WithTwoSubmissions().Parsed();

        var site = FindingsReader.ThingsToAnswerWith(model.RootElement, BrokerSnapshot.ContactArchetype)
            .Single(thing => FindingsReader.Identifier(thing) == ThisSite);

        var stated = site.GetProperty("Properties").GetProperty("statedAreaHectares");
        stated.GetProperty("value").GetDouble().Should().Be(24.0);
        stated.GetProperty("writeKind").GetString().Should().Be("FactOnly");
    }

    // The reading is rooted at one site, so the walk never reaches another submitter's — but the walk is
    // the platform's and this test is over a snapshot that holds both. What it pins is that nothing here
    // puts the other one back in.
    [Fact]
    public void Nothing_here_adds_a_thing_the_walk_did_not_reach()
    {
        using var model = BrokerSnapshot.WithTwoSubmissions().Parsed();
        var narrowed = OnlyReaching(model.RootElement, ThisSite);

        var answered = FindingsReader.ThingsToAnswerWith(narrowed.RootElement, BrokerSnapshot.ContactArchetype);

        Identifiers(answered).Should().NotContain(OtherSite);
    }

    private static IEnumerable<Guid> Identifiers(IEnumerable<JsonElement> things) =>
        things.Select(FindingsReader.Identifier);

    // The snapshot as the platform would have answered a walk rooted at one site: that site,
    // what it reaches, and the declarations. Another submitter's Things are simply absent.
    private static JsonDocument OnlyReaching(JsonElement snapshot, Guid site)
    {
        var reached = new HashSet<Guid>
        {
            site, BrokerSnapshot.StudyOf(WillowBend.SubmissionId), BrokerSnapshot.ParcelOf(WillowBend.SubmissionId),
            BrokerSnapshot.ContactOf(WillowBend.SubmissionId), BrokerSnapshot.ContactArchetype, BrokerSnapshot.Page,
            BrokerSnapshot.SiteArchetype, BrokerSnapshot.StudyArchetype, BrokerSnapshot.ParcelArchetype,
            BrokerSnapshot.IsPredicate, BrokerSnapshot.HasPredicate, BrokerSnapshot.StudiesPredicate,
        };
        return Filtered(snapshot, thing => reached.Contains(FindingsReader.Identifier(thing)));
    }

    private static JsonDocument Filtered(JsonElement snapshot, Func<JsonElement, bool> keep)
    {
        var things = snapshot.GetProperty("things").EnumerateArray().Where(keep).ToArray();
        var kept = things.Select(FindingsReader.Identifier).ToHashSet();
        var relationships = snapshot.GetProperty("relationships").EnumerateArray()
            .Where(edge => kept.Contains(Guid.Parse(edge.GetProperty("SubjectId").GetString()!))
                           && kept.Contains(Guid.Parse(edge.GetProperty("TargetId").GetString()!)))
            .ToArray();

        return JsonDocument.Parse(JsonSerializer.Serialize(
            new { watermark = 0, things, relationships },
            new JsonSerializerOptions { PropertyNamingPolicy = null }));
    }
}
