using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;

namespace vos.Service.Forage.Services;

// Works out which administrative division a site stands in, before the run fetches anything.
//
// Every route the hazard portal serves takes a division code, and none takes coordinates, so a site
// reaching no Place that carries one has every grading refused before the provider is contacted. The
// portal's own search takes a name, and answers every division of that name in every country, so the
// choosing takes the site's country — which a reshape expression cannot see and the provider cannot be
// asked to filter by. That is why this is a step in the run rather than seed data.
//
// It writes what it settled on onto the site and addresses this run's calls with it, so a site is graded
// on the run that resolved its division rather than the one after.
public sealed class DivisionResolver
{
    private readonly IEndpointBodyReader _reader;
    private readonly ICoverageWriter _writer;
    private readonly ILogger<DivisionResolver> _logger;

    public DivisionResolver(
        IEndpointBodyReader reader, ICoverageWriter writer, ILogger<DivisionResolver> logger)
    {
        _reader = reader;
        _writer = writer;
        _logger = logger;
    }

    // The coverage it was given, with the division layered into every call's address where one was worked
    // out. Unchanged where there was nothing to work out, or nothing came of it: the gradings are then
    // refused for the unfilled placeholder, their coverages stay outstanding, and the site stays in the
    // state that dispatches the run — so the next run tries again.
    public async Task<SiteCoverage> AddressAsync(
        Guid siteId, SiteCoverage coverage, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(siteId, coverage, cancellationToken) is not { } resolved) return coverage;

        return coverage with
        {
            Covering = CoveringSourceResolver.AlsoAddressedWith(coverage.Covering, resolved),
        };
    }

    private async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        Guid siteId, SiteCoverage coverage, CancellationToken cancellationToken)
    {
        if (coverage.Lookup is not { } lookup)
        {
            _logger.LogInformation(
                "The model declares no division lookup, so site {SiteId} is addressed with whatever division "
                + "its Places carry", siteId);
            return null;
        }

        // A project that coded its own Place decides. The site's own value and its Places' are already
        // layered here, so anything the model supplies keeps a run from resolving over it — and from
        // calling two providers on every run for a site that needs neither.
        if (coverage.Address.ContainsKey(lookup.CodeProperty)) return null;

        if (await _reader.ReadAsync(lookup.AreaNameEndpoint, coverage.Address, cancellationToken)
            is not { } geocoded)
            return null;

        if (HazardDivision.PositionIn(geocoded) is not { } position)
        {
            _logger.LogWarning(
                "The area-name lookup answered nothing site {SiteId} can be searched by; no division is "
                + "resolved", siteId);
            return null;
        }

        foreach (var areaName in position.AreaNames)
        {
            var searched = new Dictionary<string, string>(coverage.Address, StringComparer.OrdinalIgnoreCase)
            {
                [lookup.SearchAreaNameParameter] = areaName,
            };

            // A refused search is not a name the portal holds nothing for, so a coarser name is not asked
            // for: the run leaves the division unresolved and is driven again.
            if (await _reader.ReadAsync(lookup.SearchEndpoint, searched, cancellationToken) is not { } answered)
                return null;

            if (HazardDivision.FinestIn(HazardDivision.DivisionsIn(answered), position.Country)
                is not { } division)
                continue;

            return await WriteAsync(siteId, lookup, division, cancellationToken);
        }

        // Said out loud rather than left as an empty hazards table: the portal holds no division of any
        // name this site's position gave, in this site's country.
        _logger.LogInformation(
            "The hazard portal holds no division in {Country} for {Areas}, so site {SiteId} keeps no "
            + "division code", position.Country, string.Join(", ", position.AreaNames), siteId);
        return null;
    }

    // The code first, because it is what addresses a grading. A name written beside a code that would not
    // go would read on the page as a division the gradings never came from.
    private async Task<IReadOnlyDictionary<string, string>?> WriteAsync(
        Guid siteId, DivisionLookup lookup, PortalDivision division, CancellationToken cancellationToken)
    {
        if (!await _writer.WriteFactAsync(siteId, lookup.CodeProperty, division.Code, cancellationToken))
        {
            _logger.LogError(
                "Site {SiteId} stands in {Division}, and the code could not be written onto it; the "
                + "gradings stay outstanding and the next run resolves it again", siteId, division.FullName);
            return null;
        }

        // Not retried: the code is written, so every later run skips the lookup and the label stays
        // missing. Calling two providers again on every run for a label, while the gradings are already
        // right and traceable by the code, would cost more than the gap.
        if (!await _writer.WriteFactAsync(siteId, lookup.NameProperty, division.FullName, cancellationToken))
            _logger.LogWarning(
                "Site {SiteId} is graded at division {Code} and its name could not be written, so nothing "
                + "beside a grade says which division it is for", siteId, division.Code);

        _logger.LogInformation("Site {SiteId} stands in {Division}, graded at {Code}",
            siteId, division.FullName, division.Code);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [lookup.CodeProperty] = division.Code,
            [lookup.NameProperty] = division.FullName,
        };
    }
}
