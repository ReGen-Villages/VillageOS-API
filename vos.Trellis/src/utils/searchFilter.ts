import type { VosThing, VosRelationship } from '@tendril-ui/tendril';

export interface SearchOptions {
  caseSensitive: boolean;
  exactMatch: boolean;
  useRegex: boolean;
}

interface SearchResult {
  filteredThings: VosThing[];
  filteredRelationships: VosRelationship[];
  matchCount: number;
}

/** Shared by the Thing matcher and the label matcher, so the two agree. A query is a regular expression when
 *  that option is on and it compiles, else a comma-separated list where any term matches, else one term. */
export function buildStringMatcher(
  query: string,
  options: SearchOptions,
): (s: string) => boolean {
  if (options.useRegex) {
    try {
      const flags = options.caseSensitive ? '' : 'i';
      const re = new RegExp(query, flags);
      return (s) => re.test(s);
    } catch {
      // Text that is not a regular expression is searched as plain text.
    }
  }

  if (query.includes(',')) {
    const terms = query
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);

    if (terms.length === 0) return () => false;

    const normTerms = terms.map((t) => (options.caseSensitive ? t : t.toLowerCase()));

    return (s) => {
      const n = options.caseSensitive ? s : s.toLowerCase();
      return normTerms.some((term) =>
        options.exactMatch ? n === term : n.includes(term),
      );
    };
  }

  const q = options.caseSensitive ? query : query.toLowerCase();
  return (s) => {
    const n = options.caseSensitive ? s : s.toLowerCase();
    return options.exactMatch ? n === q : n.includes(q);
  };
}

/** Matches a Thing's identifier as well as its name, for a comma-separated list. */
function buildMatcher(
  query: string,
  options: SearchOptions,
): (thing: VosThing) => boolean {
  const matchString = buildStringMatcher(query, options);

  if (!options.useRegex && query.includes(',')) {
    const rawTerms = query.split(',').map((s) => s.trim()).filter(Boolean);
    const termSet = new Set(rawTerms);
    return (t) => termSet.has(t.Id) || matchString(t.Name);
  }

  if (options.useRegex) {
    return (t) => matchString(t.Name) || matchString(t.Id);
  }

  return (t) => matchString(t.Name);
}

export function filterGraph(
  query: string,
  things: VosThing[],
  relationships: VosRelationship[],
  options: SearchOptions,
): SearchResult {
  if (!query) return { filteredThings: things, filteredRelationships: relationships, matchCount: 0 };

  const matches = buildMatcher(query, options);

  const matchedIds = new Set(
    things.filter((t) => matches(t)).map((t) => t.Id),
  );

  const touchingRelationships = relationships.filter(
    (r) => matchedIds.has(r.SubjectId) || matchedIds.has(r.TargetId) || matchedIds.has(r.PredicateId),
  );

  // Neighbours are added so every edge has both ends, and predicates so edge labels show names, not identifiers.
  const expandedIds = new Set(matchedIds);
  for (const r of touchingRelationships) {
    expandedIds.add(r.SubjectId);
    expandedIds.add(r.TargetId);
    expandedIds.add(r.PredicateId);
  }

  const ft = things.filter((t) => expandedIds.has(t.Id));
  const ftIds = new Set(ft.map((t) => t.Id));
  const fr = relationships.filter(
    (r) => ftIds.has(r.SubjectId) && ftIds.has(r.TargetId),
  );

  const predicateIds = new Set(fr.map((r) => r.PredicateId));
  const extraPredicates = things.filter((t) => predicateIds.has(t.Id) && !ftIds.has(t.Id));
  const allFilteredThings = extraPredicates.length > 0 ? [...ft, ...extraPredicates] : ft;

  return { filteredThings: allFilteredThings, filteredRelationships: fr, matchCount: matchedIds.size };
}
