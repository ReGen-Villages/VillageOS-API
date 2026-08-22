using System.Text.Json;

namespace vos.Taproot;

/// <summary>
/// What has arrived, and what to do with it: list the submissions in this model, reject one, or promote
/// one into a project model of its own (VillageOS #6045).
///
/// Nothing here names an archetype or a predicate. A submission is whatever asserts an edge through the
/// predicate the model marks as reaching a proposed site, and the dispositions are the Things under the
/// archetype the model marks as holding them. A model that spells either differently keeps answering,
/// which is the whole reason both are Things.
/// </summary>
public class SubmissionsCommandHandler(string arg, TextWriter writer, MyceliumClient? client = null)
{
    public const string ProposedSitePredicateFlag = "__IsProposedSitePredicate";
    public const string DispositionArchetypeFlag = "__IsSubmissionDispositionArchetype";
    public const string DispositionPredicateFlag = "__IsSubmissionDispositionPredicate";

    public async Task ExecuteAsync()
    {
        if (client == null || !CommandParser.TryParseSubcommand(arg, out var subcommand, out var args))
        {
            ShowHelp();
            return;
        }

        try
        {
            switch (subcommand.ToLowerInvariant())
            {
                case "list": await ListAsync(); break;
                case "reject": await RejectAsync(args); break;
                case "promote": await PromoteAsync(args); break;
                default: ShowHelp(); break;
            }
        }
        catch (Exception exception)
        {
            writer.WriteLine($"Error: {OperatorMessage.For(exception)}");
        }
    }

    /// <summary>One submission as a reviewer needs to judge it: when it arrived, what it proposes, and what
    /// has been decided about it — or nothing, which is what waiting is.</summary>
    private sealed record Submission(
        Guid Id, string Name, string? SubmissionId, string? SubmittedAt, string? Proposes, string? Disposition);

    private async Task ListAsync()
    {
        var model = await ReadModelAsync();
        var submissions = SubmissionsIn(model).ToList();

        if (submissions.Count == 0)
        {
            writer.WriteLine("No submissions in this model.");
            return;
        }

        writer.WriteLine($"{"Arrived",-22} {"State",-10} {"Submission",-26} {"Proposes",-24} Id");
        foreach (var submission in submissions.OrderBy(one => one.SubmittedAt ?? "", StringComparer.Ordinal))
            writer.WriteLine(
                $"{submission.SubmittedAt ?? "unrecorded",-22} {submission.Disposition ?? "waiting",-10} "
                + $"{submission.SubmissionId ?? submission.Name,-26} {submission.Proposes ?? "-",-24} {submission.Id}");
    }

    private async Task RejectAsync(string[] args)
    {
        if (args.Length == 0)
        {
            writer.WriteLine("Usage: submissions reject <submission>   - move a submission to a disposable state");
            return;
        }

        var model = await ReadModelAsync();
        if (Identify(model, args[0]) is not { } submission)
            return;

        var disposable = DispositionsIn(model)
            .FirstOrDefault(one => Value(model, one.Id, "daysBeforeColdStorage") != null);
        if (disposable == default)
        {
            writer.WriteLine(
                "This model declares no disposition that names a period after which a submission goes, "
                + "so there is nothing for a rejection to mean here.");
            return;
        }

        await ResolveAsync(model, submission, disposable);
        writer.WriteLine($"Rejected: {submission.Name} is now '{disposable.Name}'.");
    }

    private async Task PromoteAsync(string[] args)
    {
        if (args.Length < 3)
        {
            writer.WriteLine(
                "Usage: submissions promote <submission> <template> <project name> [predicate,predicate]");
            writer.WriteLine(
                "  The predicates say what belongs with the site. They are the model's own vocabulary, so "
                + "they are named here rather than assumed. Default: has,studies");
            return;
        }

        var model = await ReadModelAsync();
        if (Identify(model, args[0]) is not { } submission)
            return;

        var site = ProposedSiteOf(model, submission.Id);
        if (site == null)
        {
            writer.WriteLine($"'{submission.Name}' proposes no site, so there is nothing to promote.");
            return;
        }

        var followed = args.Length > 3
            ? args[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["has", "studies"];

        var promoted = await client!.PromoteAsync(site.Value, followed, args[1], args[2]);
        CommandParser.WriteFormattedJson(writer, promoted);

        var promotedTerm = DispositionsIn(model)
            .FirstOrDefault(one => Value(model, one.Id, "daysBeforeColdStorage") == null);
        if (promotedTerm != default)
            await ResolveAsync(model, submission, promotedTerm);
    }

    /// <summary>Relate a submission to what was decided about it, and record when. Who decided is the Fact
    /// the write itself lays down, which is the record that cannot be typed in.</summary>
    private async Task ResolveAsync(ModelSnapshot model, Submission submission, (Guid Id, string Name) disposition)
    {
        if (OneCarrying(model, DispositionPredicateFlag) is not { } predicate)
        {
            writer.WriteLine($"This model marks no predicate with '{DispositionPredicateFlag}'.");
            return;
        }

        await client!.CreateRelationshipAsync(submission.Id, predicate.Id, disposition.Id);
        await client.SetPropertyAsync(
            submission.Id, "resolvedAt", "vos.DateTime", DateTime.UtcNow.ToString("O"));
    }

    private Submission? Identify(ModelSnapshot model, string named)
    {
        var submissions = SubmissionsIn(model).ToList();
        var found = Guid.TryParse(named, out var id)
            ? submissions.Where(one => one.Id == id).ToList()
            : submissions.Where(one =>
                string.Equals(one.Name, named, StringComparison.OrdinalIgnoreCase)
                || string.Equals(one.SubmissionId, named, StringComparison.OrdinalIgnoreCase)).ToList();

        switch (found.Count)
        {
            case 1: return found[0];
            case 0:
                writer.WriteLine($"No submission here answers to '{named}'.");
                return null;
            default:
                writer.WriteLine($"'{named}' answers {found.Count} submissions, so it does not say which.");
                return null;
        }
    }

    // ── Reading the model ────────────────────────────────────────────────────

    /// <summary>Everything the commands read, taken once. Properties are read effective rather than own:
    /// seed normalization moves a Thing's own values into its overrides, and a reader looking only at own
    /// properties finds a model full of Things and reads nothing off them.</summary>
    private sealed record ModelSnapshot(JsonElement Things, JsonElement Relationships, JsonElement Properties);

    private async Task<ModelSnapshot> ReadModelAsync() => new(
        await client!.GetAllThingsAsync(),
        await client.GetAllRelationshipsAsync(),
        await client.GetAllPropertiesAsync("effective"));

    private static IEnumerable<Submission> SubmissionsIn(ModelSnapshot model)
    {
        if (OneCarrying(model, ProposedSitePredicateFlag) is not { } proposes)
            yield break;

        foreach (var edge in EdgesThrough(model, proposes.Id))
        {
            var id = Subject(edge);
            yield return new Submission(
                id,
                NameOf(model, id) ?? id.ToString(),
                Value(model, id, "submissionId"),
                Value(model, id, "submittedAt"),
                NameOf(model, Target(edge)),
                DispositionOf(model, id));
        }
    }

    private static Guid? ProposedSiteOf(ModelSnapshot model, Guid submission) =>
        OneCarrying(model, ProposedSitePredicateFlag) is { } proposes
            ? EdgesThrough(model, proposes.Id).Where(edge => Subject(edge) == submission)
                .Select(edge => (Guid?)Target(edge)).FirstOrDefault()
            : null;

    private static string? DispositionOf(ModelSnapshot model, Guid submission) =>
        OneCarrying(model, DispositionPredicateFlag) is { } resolvedAs
            ? EdgesThrough(model, resolvedAs.Id).Where(edge => Subject(edge) == submission)
                .Select(edge => NameOf(model, Target(edge))).FirstOrDefault()
            : null;

    /// <summary>The Things under the archetype the model marks as holding what a submission can be resolved
    /// to. Found by the mark, never by the archetype's name.</summary>
    private static IEnumerable<(Guid Id, string Name)> DispositionsIn(ModelSnapshot model)
    {
        if (OneCarrying(model, DispositionArchetypeFlag) is not { } archetype)
            yield break;

        foreach (var edge in model.Relationships.EnumerateArray())
        {
            if (Target(edge) != archetype.Id) continue;
            if (NameOf(model, Predicate(edge)) != "is") continue;
            var id = Subject(edge);
            yield return (id, NameOf(model, id) ?? id.ToString());
        }
    }

    /// <summary>The one Thing carrying a mark. More than one leaves a reader with two answers and no way to
    /// choose, so it answers with none rather than picking.</summary>
    private static (Guid Id, string Name)? OneCarrying(ModelSnapshot model, string flag)
    {
        var carrying = model.Properties.EnumerateObject()
            .Where(entry => entry.Value.TryGetProperty(flag, out _))
            .Select(entry => Guid.TryParse(entry.Name, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        return carrying.Count == 1 ? (carrying[0], NameOf(model, carrying[0]) ?? "") : null;
    }

    private static IEnumerable<JsonElement> EdgesThrough(ModelSnapshot model, Guid predicate) =>
        model.Relationships.EnumerateArray().Where(edge => Predicate(edge) == predicate);

    private static Guid Subject(JsonElement edge) => Identifier(edge, "SubjectId");

    private static Guid Predicate(JsonElement edge) => Identifier(edge, "PredicateId");

    private static Guid Target(JsonElement edge) => Identifier(edge, "TargetId");

    private static Guid Identifier(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetGuid(out var id) ? id : Guid.Empty;

    private static string? NameOf(ModelSnapshot model, Guid id) =>
        model.Things.EnumerateArray()
            .Where(thing => Identifier(thing, "Id") == id)
            .Select(thing => thing.TryGetProperty("Name", out var name) ? name.GetString() : null)
            .FirstOrDefault();

    /// <summary>A property as text, whatever it is written as, because everything here is displayed.</summary>
    private static string? Value(ModelSnapshot model, Guid thing, string property)
    {
        if (!model.Properties.TryGetProperty(thing.ToString(), out var properties)) return null;
        if (!properties.TryGetProperty(property, out var held)) return null;

        var value = held.TryGetProperty("Value", out var inner) ? inner : held;
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.ToString(),
        };
    }

    private void ShowHelp()
    {
        writer.WriteLine("Submission review commands:");
        writer.WriteLine("  submissions list                                      - what has arrived, and its state");
        writer.WriteLine("  submissions reject <submission>                       - move one to a disposable state");
        writer.WriteLine("  submissions promote <submission> <template> <name> [predicates]");
        writer.WriteLine("                                                        - copy one into a project model of its own");
        writer.WriteLine();
        writer.WriteLine("  A submission is identified by its identifier, its name, or the identifier it was");
        writer.WriteLine("  submitted under. Promoting twice produces one project.");
    }
}
