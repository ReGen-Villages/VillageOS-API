import type { VosThing, VosRelationship } from '../types/vos';

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

/**
 * Build a string matcher from the query and options.
 *
 * Supports three modes (checked in order):
 * 1. **Regex** — query compiled as RegExp; invalid regex falls through to plain text.
 * 2. **Comma-separated list** — split into trimmed terms, any must match.
 * 3. **Single term** — plain substring or exact-match.
 *
 * This is the shared core used by both `buildMatcher` (thing-level, checks
 * Name + Id) and `buildLabelMatcher` in nodeVisibility (label strings).
 */
export function buildStringMatcher(
  query: string,
  options: SearchOptions,
): (s: string) => boolean {
  // ── Regex mode ──────────────────────────────────────────────────────
  if (options.useRegex) {
    try {
      const flags = options.caseSensitive ? '' : 'i';
      const re = new RegExp(query, flags);
      return (s) => re.test(s);
    } catch {
      // Invalid regex — fall through to plain text
    }
  }

  // ── Comma-separated list mode ───────────────────────────────────────
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

  // ── Single-term mode ────────────────────────────────────────────────
  const q = options.caseSensitive ? query : query.toLowerCase();
  return (s) => {
    const n = options.caseSensitive ? s : s.toLowerCase();
    return options.exactMatch ? n === q : n.includes(q);
  };
}

/**
 * Build a thing-level matcher that checks Name and Id.
 * Delegates string matching to `buildStringMatcher`, then adds Id lookup
 * for comma-separated lists.
 */
function buildMatcher(
  query: string,
  options: SearchOptions,
): (thing: VosThing) => boolean {
  const matchString = buildStringMatcher(query, options);

  // For comma-separated lists, also do exact ID matching
  if (!options.useRegex && query.includes(',')) {
    const rawTerms = query.split(',').map((s) => s.trim()).filter(Boolean);
    const termSet = new Set(rawTerms);
    return (t) => termSet.has(t.Id) || matchString(t.Name);
  }

  // For regex mode, test both Name and Id
  if (options.useRegex) {
    return (t) => matchString(t.Name) || matchString(t.Id);
  }

  // Single-term: name only (original behaviour)
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

  // Find all relationships that touch a matched node
  const touchingRels = relationships.filter(
    (r) => matchedIds.has(r.SubjectId) || matchedIds.has(r.TargetId) || matchedIds.has(r.PredicateId),
  );

  // Expand to include neighbor nodes so edges always have both endpoints,
  // and predicate things so edge labels resolve to names instead of GUIDs
  const expandedIds = new Set(matchedIds);
  for (const r of touchingRels) {
    expandedIds.add(r.SubjectId);
    expandedIds.add(r.TargetId);
    expandedIds.add(r.PredicateId);
  }

  const ft = things.filter((t) => expandedIds.has(t.Id));
  const ftIds = new Set(ft.map((t) => t.Id));
  // Only include edges where BOTH endpoints are present
  const fr = relationships.filter(
    (r) => ftIds.has(r.SubjectId) && ftIds.has(r.TargetId),
  );

  // Also include predicate things for any remaining filtered relationships
  // so the mapper can always resolve predicate names
  const predicateIds = new Set(fr.map((r) => r.PredicateId));
  const extraPredicates = things.filter((t) => predicateIds.has(t.Id) && !ftIds.has(t.Id));
  const allFilteredThings = extraPredicates.length > 0 ? [...ft, ...extraPredicates] : ft;

  return { filteredThings: allFilteredThings, filteredRelationships: fr, matchCount: matchedIds.size };
}
