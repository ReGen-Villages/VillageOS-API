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
    private static readonly Regex EdgeLabel = new(@"-->\|(?<predicate>[^|]+)\|", RegexOptions.Compiled);

    [Fact]
    public void Every_edge_in_the_archetype_diagram_names_a_predicate_the_composer_writes()
    {
        var diagram = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "land-intake-archetypes.mmd"));
        var composed = new[]
        {
            SubmissionFragmentComposer.HasPredicateName,
            SubmissionFragmentComposer.StudiesPredicateName,
        };

        var drawn = EdgeLabel.Matches(diagram)
            .Select(match => match.Groups["predicate"].Value)
            .ToList();

        drawn.Should().NotBeEmpty("a diagram whose edges cannot be read proves nothing about the ones it draws");
        drawn.Distinct().Where(predicate => !composed.Contains(predicate)).Should().BeEmpty(
            $"the diagram may only draw predicates the model holds — {string.Join(", ", composed)}");
    }
}
