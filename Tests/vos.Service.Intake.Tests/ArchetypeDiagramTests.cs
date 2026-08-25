using System.Text.RegularExpressions;
using FluentAssertions;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>
/// The archetype diagram is what a reader builds a submission against, and a predicate drawn there that
/// nothing mints fails quietly: the fragment is accepted, the Things appear, and every reader that walks
/// the real predicate finds a site with nothing hanging off it.
/// </summary>
public class ArchetypeDiagramTests
{
    private const string DesignDocumentFileName = "LAND_INTAKE.md";
    private const string ArchetypeSectionHeading = "### The archetypes";

    private static readonly Regex Arrow = new(@"[-=.]{2,}[>ox]", RegexOptions.Compiled);

    private static readonly Regex LabelledArrow =
        new(@"[-=.]{2,}[>ox]\s*\|(?<predicate>[^|]+)\|", RegexOptions.Compiled);

    private static readonly Regex DiagramUnderArchetypeHeading = new(
        "^" + Regex.Escape(ArchetypeSectionHeading) + @"\s*\r?\n(?:(?!^#{1,6} ).)*?^```mermaid\r?\n(?<diagram>.*?)^```",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

    [Fact]
    public void Every_edge_in_the_archetype_diagram_names_a_predicate_the_composer_writes()
    {
        var diagram = ArchetypeDiagram();

        var namedByTheComposer = new[]
        {
            SubmissionFragmentComposer.HasPredicateName,
            SubmissionFragmentComposer.StudiesPredicateName,
            SubmissionFragmentComposer.ProposesPredicateName,
        };

        // Written by the composer but never named by it: it finds each by the mark the model puts on it, so
        // these are the land-intake template's spellings rather than this service's. A template that renamed
        // one leaves the drawing describing a spelling the model no longer uses and breaks nothing at
        // runtime, which is why they are kept apart from those above. `resolvedAs` joins them as a spelling
        // the model owns: no submission writes it, and the edge is drawn because it is what a reviewer's
        // decision becomes.
        var spelledByTheTemplate = new[] { "categorizedAs", "obtainedBy", "assesses", "resolvedAs" };
        var mayBeDrawn = namedByTheComposer.Concat(spelledByTheTemplate).ToList();

        var drawn = LabelledArrow.Matches(diagram)
            .Select(match => match.Groups["predicate"].Value)
            .ToList();

        drawn.Should().NotBeEmpty("a diagram whose edges cannot be read proves nothing about the ones it draws");
        drawn.Should().HaveCount(Arrow.Matches(diagram).Count,
            "Mermaid draws an arrow several ways and carries its label two ways, so an edge this test "
            + "cannot read a label from has to fail here rather than go unchecked");
        drawn.Distinct().Where(predicate => !mayBeDrawn.Contains(predicate)).Should().BeEmpty(
            $"the diagram may only draw predicates the model holds — {string.Join(", ", mayBeDrawn)}");
    }

    private static string ArchetypeDiagram()
    {
        var document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, DesignDocumentFileName));
        var match = DiagramUnderArchetypeHeading.Match(document);

        match.Success.Should().BeTrue(
            $"the diagram is written into {DesignDocumentFileName} under '{ArchetypeSectionHeading}' rather "
            + "than rendered to a picture, so moving it or retitling the section has to fail here rather "
            + "than leave this test reading nothing and passing");

        return match.Groups["diagram"].Value;
    }
}
