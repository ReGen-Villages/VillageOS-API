using System.Globalization;
using System.Text.Json;

namespace vos.Taproot;

/// <summary>
/// What has arrived, and what to do with it: list the submissions in this model, reject one, promote one
/// into a project model of its own (VillageOS #6045), or clear the rejections whose period has run
/// (VillageOS #6657).
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

    /// <summary>The platform's one canonical predicate, and the only predicate name a reader may hold: it
    /// is the platform's own vocabulary rather than any model's, and nothing marks it.</summary>
    private const string IsPredicateName = "is";

    /// <summary>The period after which a submission resolved to this disposition goes. Which disposition is
    /// disposable is read off the model as the one naming it, so a model spelling `rejected` differently
    /// still says what a rejection means.</summary>
    private const string ColdStoragePeriodProperty = "daysBeforeColdStorage";

    /// <summary>When a submission was decided about, which is what its period is counted from.</summary>
    private const string ResolvedAtProperty = "resolvedAt";

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
                case "dispose": await DisposeAsync(args); break;
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
        Guid Id, string Name, string? SubmissionId, string? SubmittedAt,
        Guid ProposedSite, string? ProposedSiteName, Guid? Disposition);

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
        {
            // "handled" is the model's own word for a submission a disposition has been related to, and is
            // what a disposition too nameless to display leaves the reader with.
            var state = submission.Disposition is { } decided ? NameOf(model, decided) ?? "handled" : "waiting";
            writer.WriteLine(
                $"{submission.SubmittedAt ?? "unrecorded",-22} {state,-10} "
                + $"{submission.SubmissionId ?? submission.Name,-26} {submission.ProposedSiteName ?? "-",-24} {submission.Id}");
        }
    }

    private async Task RejectAsync(string[] args)
    {
        if (args.Length == 0)
        {
            writer.WriteLine("Usage: submissions reject <submission>   - move a submission to a disposable state");
            return;
        }

        var model = await ReadModelAsync();
        // Everything after the subcommand, because a submission's name is the site's followed by a word and
        // the parser splits on spaces. Nothing follows it, so there is nothing to take the rest from it.
        if (Identify(model, string.Join(' ', args)) is not { } submission)
            return;

        var disposable = DispositionsIn(model)
            .FirstOrDefault(one => Value(model, one.Id, ColdStoragePeriodProperty) != null);
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
        if (args.Length < 4)
        {
            writer.WriteLine("Usage: submissions promote <submission> <template> <predicates> <project name>");
            writer.WriteLine(
                "  <predicates> is a comma-separated list saying what belongs with the site. They are the "
                + "model's own vocabulary, so they are named rather than assumed.");
            writer.WriteLine("  The submission is given as an identifier here, because the name is what takes");
            writer.WriteLine("  the rest of the line. Reject takes a name.");
            return;
        }

        var model = await ReadModelAsync();
        if (Identify(model, args[0]) is not { } submission)
            return;

        var followed = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The project's name is whatever is left, so a model can be called what a planner would call it.
        var promoted = await client!.PromoteAsync(
            submission.ProposedSite, followed, args[1], string.Join(' ', args[3..]));
        CommandParser.WriteFormattedJson(writer, promoted);

        var promotedTerm = DispositionsIn(model)
            .FirstOrDefault(one => Value(model, one.Id, ColdStoragePeriodProperty) == null);
        if (promotedTerm != default)
            await ResolveAsync(model, submission, promotedTerm);
    }

    /// <summary>
    /// The retention pass: every rejected submission whose period has run leaves the model, and everything
    /// it minted goes with it.
    ///
    /// The walk starts at the record of the arrival rather than at the site, because that record is intake's
    /// own and a promotion deliberately leaves it behind. It reaches the site through the predicate the model
    /// marks, so only what hangs off the site has to be named.
    ///
    /// Which submissions are due is the model's answer, not this tool's: the disposition names the period, and
    /// the submission carries the instant it was decided. A submission nobody has dealt with is kept
    /// indefinitely, and so is one resolved to a disposition naming no period.
    /// </summary>
    private async Task DisposeAsync(string[] args)
    {
        if (args.Length == 0)
        {
            writer.WriteLine("Usage: submissions dispose <predicates>   - clear every rejected submission whose period has run");
            writer.WriteLine(
                "  <predicates> is a comma-separated list saying what belongs with the site, as promote takes "
                + "it. The predicate reaching the site is added to it: that one the model marks.");
            return;
        }

        var model = await ReadModelAsync();
        if (OneOwning(model, ProposedSitePredicateFlag) is not { } proposes)
        {
            writer.WriteLine($"This model marks no predicate with '{ProposedSitePredicateFlag}'.");
            return;
        }

        string[] followed =
        [
            .. args[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            proposes.Name,
        ];

        var now = DateTime.UtcNow;
        var taken = 0;
        var notYetDue = 0;

        foreach (var submission in SubmissionsIn(model).ToList())
        {
            if (submission.Disposition is not { } decided) continue;
            if (Value(model, decided, ColdStoragePeriodProperty) is not { } period) continue;

            if (!double.TryParse(period, NumberStyles.Any, CultureInfo.InvariantCulture, out var days))
            {
                writer.WriteLine(
                    $"'{NameOf(model, decided)}' names '{period}' as its period, which is not a number of days, "
                    + $"so {submission.Name} was left where it is.");
                continue;
            }

            if (Value(model, submission.Id, ResolvedAtProperty) is not { } when
                // Assumed universal as well as adjusted to it: the platform writes instants in UTC, and an
                // instant written without a zone would otherwise be read as the operator's local time.
                || !DateTime.TryParse(when, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var resolved))
            {
                writer.WriteLine(
                    $"{submission.Name} carries no instant this can read as when it was decided, so there is "
                    + "nowhere to count its period from and it was left where it is.");
                continue;
            }

            if (resolved.AddDays(days) > now)
            {
                notYetDue++;
                continue;
            }

            var removed = await client!.PruneAsync(submission.Id, followed);
            taken++;
            foreach (var one in Removed(removed))
                writer.WriteLine($"  {one}");
        }

        writer.WriteLine(taken == 0
            ? "Nothing here is due for disposal."
            : $"Took {taken} submission(s) out of this model.");
        if (notYetDue > 0)
            writer.WriteLine($"{notYetDue} rejected submission(s) not yet due.");
    }

    /// <summary>What a prune says it took, as the broker named it. Read from the answer rather than from what
    /// was asked for, because the reach is the broker's to decide and a submission may have grown since.
    /// </summary>
    private static IEnumerable<string> Removed(JsonElement pruned)
    {
        if (!pruned.TryGetProperty("removed", out var removed) || removed.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var one in removed.EnumerateArray())
            yield return one.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
    }

    /// <summary>Relate a submission to what was decided about it, and record when. Who decided is the Fact
    /// the write itself lays down, which is the record that cannot be typed in.</summary>
    private async Task ResolveAsync(ModelSnapshot model, Submission submission, (Guid Id, string Name) disposition)
    {
        if (OneOwning(model, DispositionPredicateFlag) is not { } predicate)
        {
            writer.WriteLine($"This model marks no predicate with '{DispositionPredicateFlag}'.");
            return;
        }

        await client!.CreateRelationshipAsync(submission.Id, predicate.Id, disposition.Id);
        await client.SetPropertyAsync(
            submission.Id, ResolvedAtProperty, "vos.DateTime", DateTime.UtcNow.ToString("O"));
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
        if (OneOwning(model, ProposedSitePredicateFlag) is not { } proposes)
            yield break;

        // One submission per edge: a submission is only a submission because it proposes a site, so the
        // walk that finds it is also the walk that says which site travels when it is promoted. A model
        // declares that predicate by relating its own archetypes, and that edge is asserted through the
        // same predicate — listed, it offers a reviewer a decision over the declaration itself.
        foreach (var edge in EdgesThrough(model, proposes.Id))
        {
            var id = Subject(edge);
            if (IsArchetype(model, id)) continue;
            yield return new Submission(
                id,
                NameOf(model, id) ?? id.ToString(),
                Value(model, id, "submissionId"),
                Value(model, id, "submittedAt"),
                Target(edge),
                NameOf(model, Target(edge)),
                DispositionOf(model, id));
        }
    }

    private static Guid? DispositionOf(ModelSnapshot model, Guid submission) =>
        OneOwning(model, DispositionPredicateFlag) is { } resolvedAs
            ? EdgesThrough(model, resolvedAs.Id).Where(edge => Subject(edge) == submission)
                .Select(edge => (Guid?)Target(edge)).FirstOrDefault()
            : null;

    /// <summary>The Things under the archetype the model marks as holding what a submission can be resolved
    /// to. Found by the mark, never by the archetype's name.</summary>
    private static IEnumerable<(Guid Id, string Name)> DispositionsIn(ModelSnapshot model)
    {
        if (OneOwning(model, DispositionArchetypeFlag) is not { } archetype)
            yield break;

        foreach (var edge in model.Relationships.EnumerateArray())
        {
            if (Target(edge) != archetype.Id) continue;
            if (NameOf(model, Predicate(edge)) != IsPredicateName) continue;
            var id = Subject(edge);
            yield return (id, NameOf(model, id) ?? id.ToString());
        }
    }

    /// <summary>The one Thing that owns a mark. More than one leaves a reader with two answers and no way
    /// to choose, so it answers with none rather than picking.</summary>
    private static (Guid Id, string Name)? OneOwning(ModelSnapshot model, string flag)
    {
        var owning = model.Properties.EnumerateObject()
            .Where(entry => Owns(entry.Value, flag))
            .Select(entry => Guid.TryParse(entry.Name, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        return owning.Count == 1 ? (owning[0], NameOf(model, owning[0]) ?? "") : null;
    }

    /// <summary>Owned, not merely present. Properties are read effective, and a mark is an ordinary
    /// property on the archetype, so every term that `is` it reads the mark too. Counting every carrier
    /// finds the archetype and all of its terms, and a vocabulary then reads as ambiguous the moment it
    /// has any terms at all — which is every seeded model.</summary>
    private static bool Owns(JsonElement properties, string flag) =>
        properties.TryGetProperty(flag, out var mark)
        && mark.TryGetProperty("IsInherited", out var inherited)
        && inherited.ValueKind == JsonValueKind.False;

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

    private static bool IsArchetype(ModelSnapshot model, Guid id) =>
        model.Things.EnumerateArray()
            .Where(thing => Identifier(thing, "Id") == id)
            .Any(thing => thing.TryGetProperty("IsArchetype", out var archetype)
                          && archetype.ValueKind == JsonValueKind.True);

    /// <summary>A property as text, whatever it is written as, because everything here is displayed.
    ///
    /// A Thing's own value is keyed by the bare name, but a value it holds for a name its archetype
    /// declares comes back keyed by that archetype — <c>Submission.submittedAt</c> rather than
    /// <c>submittedAt</c>. Both are the same property to a reader, so the name is matched after its
    /// declaring prefix.</summary>
    private static string? Value(ModelSnapshot model, Guid thing, string property)
    {
        if (!model.Properties.TryGetProperty(thing.ToString(), out var properties)) return null;

        var match = properties.EnumerateObject()
            .Where(held => held.Name.Split('.')[^1] == property)
            .Select(held => (JsonElement?)held.Value)
            .FirstOrDefault();
        if (match is not { } held) return null;

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
        writer.WriteLine("  submissions promote <submission> <template> <predicates> <project name>");
        writer.WriteLine("                                                        - copy one into a project model of its own");
        writer.WriteLine("  submissions dispose <predicates>                      - clear every rejection whose period has run");
        writer.WriteLine();
        writer.WriteLine("  A submission is identified by its identifier, its name, or the identifier it was");
        writer.WriteLine("  submitted under. A name may contain spaces where it ends the line — which it does");
        writer.WriteLine("  for reject, and does not for promote. Promoting twice produces one project.");
    }
}
