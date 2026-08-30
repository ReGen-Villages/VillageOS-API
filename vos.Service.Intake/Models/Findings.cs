using System.Text.Json;

namespace vos.Service.Intake.Models;

/// <summary>
/// What a submitter is answered with: the page the model declares, the site it is drawn about, and the
/// reading it is drawn from.
/// </summary>
/// <remarks>
/// The Things and relationships travel exactly as the broker wrote them, rather than being read into a
/// shape here and written out again. What draws them is the same resolver the signed-in application
/// runs, so it reads the same envelope — a value's declared write kind, which is what says whether a
/// figure was stated or measured, and how a computed value is worked out. A shape of this service's own
/// would carry the fields somebody thought of, and quietly drop the next one the model starts using.
/// </remarks>
/// <param name="Ranges">The judge-ranges of every Thing in the answer holding a state, keyed by
/// identifier. A verdict reads its target off the comparison the range makes, and those ranges sit on an
/// archetype rather than on the Thing, so a reading alone cannot answer them.</param>
public sealed record Findings(
    string Spec,
    Guid ScopeId,
    IReadOnlyList<JsonElement> Things,
    IReadOnlyList<JsonElement> Relationships,
    IReadOnlyDictionary<string, JsonElement> Ranges);

/// <summary>What the model says about answering somebody who holds no account: which page they may read,
/// which Things never travel, and — for the one submission being asked about — the address it names.
/// </summary>
public sealed record SubmitterDeclarations(string Spec, Guid PersonalDetailArchetype, string? ContactAddress);
