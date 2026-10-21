using System.Globalization;
using System.Text;
using System.Text.Json;

namespace vos.Service.Forage.Helpers;

// What a reverse geocoder answered about a position: the country the land is in, the names it gave for
// the area around it, finest first, and the standard's codes for the region and the country, finest first.
public sealed record GeocodedPosition(
    string Country, IReadOnlyList<string> AreaNames, IReadOnlyList<string> StandardCodes);

// One division the hazard portal holds, as its search answers it: the code every route it serves is
// addressed with, and the levels the division sits under from the country down.
public sealed record PortalDivision(string Code, IReadOnlyList<string> Levels)
{
    public string Country => Levels[0];

    // What a reader needs beside a grade. The portal's per-hazard reply names no division at all, and one
    // name exists at two levels graded differently, so the code alone leaves the two indistinguishable.
    public string FullName => string.Join(" / ", Levels);
}

// Reads the two replies a run works a site's administrative division out from, and picks between them.
//
// Nothing here reaches the network or the model: which addresses are called is the model's, and both
// replies are provider shapes this turns into the one answer a hazard call needs.
public static class HazardDivision
{
    private const string GeocodedAddressField = "address";
    private const string GeocodedCountryField = "country";

    // The county before the state. The same name exists as a region and as a district inside it, graded
    // differently — Portugal / Santarem reads high river flood as the region and medium as the district —
    // and the finer division is the land the site is actually in. Both are searched rather than only the
    // finer: a geocoder answering "Dukes County" where the portal holds "Dukes" finds nothing, and the
    // state it also gave is a division the portal does hold.
    private static readonly string[] GeocodedAreaFields = ["county", "state"];

    // A region's code is answered under the standard's level number, `ISO3166-2-lvl4` for a Polish
    // voivodeship, and a finer region under a higher one. The country's two letters arrive in lower case.
    private const string GeocodedRegionCodeFieldPrefix = "ISO3166-2-lvl";
    private const string GeocodedCountryCodeField = "country_code";

    private const string SearchResultsField = "data";
    private const string SearchCodeField = "code";

    // The portal names a division by the levels it sits under, country first. A reply naming more of them
    // is a division inside the ones naming fewer.
    private static readonly string[] SearchLevelFields = ["admin0", "admin1", "admin2"];

    // Null where the reply names no country or no area at all: a search needs both, and one without the
    // other would either ask about nowhere or keep every country's answer.
    public static GeocodedPosition? PositionIn(string reply)
    {
        if (ObjectIn(reply) is not { } root) return null;
        if (!root.TryGetProperty(GeocodedAddressField, out var address)
            || address.ValueKind != JsonValueKind.Object)
            return null;

        if (TextIn(address, GeocodedCountryField) is not { } country) return null;

        var areaNames = GeocodedAreaFields
            .Select(field => TextIn(address, field))
            .OfType<string>()
            .ToList();

        return areaNames.Count == 0 ? null : new GeocodedPosition(country, areaNames, StandardCodesIn(address));
    }

    private static List<string> StandardCodesIn(JsonElement address)
    {
        var codes = address.EnumerateObject()
            .Where(field => field.Name.StartsWith(GeocodedRegionCodeFieldPrefix, StringComparison.Ordinal))
            .Select(field => (
                Level: int.TryParse(field.Name[GeocodedRegionCodeFieldPrefix.Length..], out var level) ? level : -1,
                Code: TextIn(address, field.Name)))
            .Where(region => region.Level >= 0 && region.Code is not null)
            .OrderByDescending(region => region.Level)
            .Select(region => region.Code!)
            .ToList();

        if (TextIn(address, GeocodedCountryCodeField) is { } country) codes.Add(country.ToUpperInvariant());
        return codes;
    }

    // Every division the search answered, in the order it gave them. An entry naming no code or no
    // country is left out: neither can address a call, and neither can be told apart from another
    // country's.
    public static IReadOnlyList<PortalDivision> DivisionsIn(string reply)
    {
        if (ObjectIn(reply) is not { } root) return [];
        if (!root.TryGetProperty(SearchResultsField, out var results)
            || results.ValueKind != JsonValueKind.Array)
            return [];

        var divisions = new List<PortalDivision>();
        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object) continue;
            if (TextIn(result, SearchCodeField) is not { } code) continue;

            var levels = new List<string>();
            foreach (var field in SearchLevelFields)
            {
                // The levels run outwards from the country, so one the reply skips ends the division
                // rather than closing up: a name read at the wrong level would order two divisions wrongly.
                if (TextIn(result, field) is not { } level) break;
                levels.Add(level);
            }

            if (levels.Count > 0) divisions.Add(new PortalDivision(code, levels));
        }

        return divisions;
    }

    // A word shorter than this is an article or a preposition more often than a name, and the portal
    // answers every division whose name contains the text searched.
    private const int ShortestWordSearched = 4;

    // Everything the portal is searched by, in order: for each level, finest first, the English name, the
    // local name, then each word of either. Whole names first because a word finds more; the local name's
    // words before the English name's because the portal holds a division under its own name.
    public static IReadOnlyList<string> SearchTermsFor(
        IReadOnlyList<string> englishNames, IReadOnlyList<string> localNames)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string term)
        {
            if (seen.Add(WithoutDiacritics(term))) terms.Add(term);
        }

        for (var level = 0; level < Math.Max(englishNames.Count, localNames.Count); level++)
        {
            var english = level < englishNames.Count ? englishNames[level] : null;
            var local = level < localNames.Count ? localNames[level] : null;
            if (english is not null) Add(english);
            if (local is not null) Add(local);

            foreach (var name in new[] { local, english })
                foreach (var word in (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (word.Length >= ShortestWordSearched) Add(word);
        }

        return terms;
    }

    // A search for one name answers every division of that name in every country — `Santarem` answers one
    // in Portugal and two in Brazil — so the country is what tells the site's land from somewhere else's.
    // And it answers every division whose name contains the text: `mazowieckie` answers Mazowieckie and
    // the district Wysokie Mazowieckie in another voivodeship, so only a division named exactly what was
    // searched is a candidate. Two standing equally deep are a tie nothing here can settle, and a guessed
    // division reads exactly like a resolved one, so neither is taken.
    public static PortalDivision? FinestIn(IReadOnlyList<PortalDivision> divisions, string country, string searched)
    {
        var named = divisions
            .Where(division => NamesTheSameCountry(division.Country, country))
            .Where(division => NamesTheSame(division.Levels[^1], searched))
            .ToList();
        if (named.Count == 0) return null;

        var deepest = named.Max(division => division.Levels.Count);
        var finest = named.Where(division => division.Levels.Count == deepest).ToList();

        return finest.Count == 1 ? finest[0] : null;
    }

    // The portal writes every name without its accents and the geocoder keeps them even in English:
    // `Santarém` against `Santarem`. The portal's own search already matches the two.
    private static bool NamesTheSame(string held, string searched) =>
        string.Equals(WithoutDiacritics(held), WithoutDiacritics(searched), StringComparison.OrdinalIgnoreCase);

    private static string WithoutDiacritics(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var letters = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                letters.Append(character);
        return letters.ToString();
    }

    // One name starting with the other, because the two providers do not agree on a country's full name:
    // the geocoder says `United States` where the portal holds `United States of America`.
    private static bool NamesTheSameCountry(string held, string answered) =>
        held.StartsWith(answered, StringComparison.OrdinalIgnoreCase)
        || answered.StartsWith(held, StringComparison.OrdinalIgnoreCase);

    private static JsonElement? ObjectIn(string reply)
    {
        try
        {
            var root = JsonDocument.Parse(reply).RootElement;
            return root.ValueKind == JsonValueKind.Object ? root : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A code arrives as a number and a name as a string, and both are read as the text a placeholder is
    // filled with. Blank is nothing: a level the provider left empty is a level it did not answer.
    private static string? TextIn(JsonElement holder, string field)
    {
        if (!holder.TryGetProperty(field, out var value)) return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
