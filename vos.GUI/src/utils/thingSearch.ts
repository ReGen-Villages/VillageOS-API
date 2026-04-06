import type { VosThing, VosRelationship } from '../types/vos';
import { formatPropertyValue } from './formatters';

/** Property keys excluded from the preview and own-property count. */
export const THING_SEARCH_SKIP_KEYS = new Set([
  'geometry',
  'footprint',
  '__geometry_envelope',
  '__IsSurface',
]);

/** Max own properties shown inline per result row. */
export const PREVIEW_PROPS = 4;

export interface ThingMatch {
  id: string;
  name: string;
  /** Relevance tier: 0 = exact, 1 = prefix, 2 = substring, 3 = ID match. Lower is better. */
  score: number;
  typeName?: string;
  ownPropertyCount: number;
  relationshipCount: number;
  previewProps: Array<{ key: string; value: unknown; formatted: string }>;
}

export interface ThingSearchIndex {
  /** Maps subject ID → type name from the first "is" relationship found. */
  isSubjectToTypeName: Map<string, string>;
  /** Maps thing ID → number of relationships where it appears as subject or target. */
  relCountByThing: Map<string, number>;
}

/**
 * Pre-compute indexes from model data so `searchThings` can run in O(N).
 * Call once when `things` or `relationships` change (e.g., in a `useMemo`).
 */
export function buildThingSearchIndex(
  things: VosThing[],
  relationships: VosRelationship[],
): ThingSearchIndex {
  const thingMap = new Map(things.map((t) => [t.Id, t]));
  const isSubjectToTypeName = new Map<string, string>();
  const relCountByThing = new Map<string, number>();

  for (const r of relationships) {
    relCountByThing.set(r.SubjectId, (relCountByThing.get(r.SubjectId) ?? 0) + 1);
    relCountByThing.set(r.TargetId, (relCountByThing.get(r.TargetId) ?? 0) + 1);

    const pred = thingMap.get(r.PredicateId);
    if (pred && pred.Name.toLowerCase() === 'is') {
      if (!isSubjectToTypeName.has(r.SubjectId)) {
        const targetName = thingMap.get(r.TargetId)?.Name;
        if (targetName) isSubjectToTypeName.set(r.SubjectId, targetName);
      }
    }
  }

  return { isSubjectToTypeName, relCountByThing };
}

/**
 * Score a thing's name against the query.
 *
 * Returns:
 * - `0` — exact name match
 * - `1` — name starts with query
 * - `2` — name contains query (substring)
 * - `3` — ID contains query
 * - `null` — no match
 *
 * All comparisons are case-insensitive.
 */
export function scoreThingMatch(thing: VosThing, query: string): number | null {
  const q = query.toLowerCase();
  const name = thing.Name.toLowerCase();
  const id = thing.Id.toLowerCase();

  if (name === q) return 0;
  if (name.startsWith(q)) return 1;
  if (name.includes(q)) return 2;
  if (id.includes(q)) return 3;
  return null;
}

/**
 * Search things by name (and ID fallback), returning ranked matches.
 *
 * Results are sorted by score ascending (best first), then alphabetically
 * within the same score tier.
 */
export function searchThings(
  query: string,
  things: VosThing[],
  index: ThingSearchIndex,
): ThingMatch[] {
  const q = query.trim();
  if (q.length === 0) return [];

  const matches: ThingMatch[] = [];

  for (const thing of things) {
    const score = scoreThingMatch(thing, q);
    if (score === null) continue;

    const ownKeys = Object.keys(thing.Properties).filter((k) => !THING_SEARCH_SKIP_KEYS.has(k));
    const previewProps = ownKeys.slice(0, PREVIEW_PROPS).map((key) => ({
      key,
      value: thing.Properties[key],
      formatted: formatPropertyValue(thing.Properties[key]),
    }));

    matches.push({
      id: thing.Id,
      name: thing.Name,
      score,
      typeName: index.isSubjectToTypeName.get(thing.Id),
      ownPropertyCount: ownKeys.length,
      relationshipCount: index.relCountByThing.get(thing.Id) ?? 0,
      previewProps,
    });
  }

  matches.sort((a, b) => a.score - b.score || a.name.localeCompare(b.name));
  return matches;
}

/** Build a markdown table from a set of search results. */
export function buildThingSearchMarkdown(query: string, results: ThingMatch[]): string {
  const lines: string[] = [`# Thing Search: "${query}"`, ''];
  lines.push(
    '| Name | Type | Properties | Relationships |',
    '|------|------|------------|---------------|',
  );
  for (const m of results) {
    lines.push(`| ${m.name} | ${m.typeName ?? '—'} | ${m.ownPropertyCount} | ${m.relationshipCount} |`);
  }
  return lines.join('\n');
}
