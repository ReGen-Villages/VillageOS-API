import type { VosThing, VosRelationship } from '../types/vos';
import i18n from '../i18n';

/** Property keys excluded from the preview and own-property count. */
export const THING_SEARCH_SKIP_KEYS = new Set([
  'geometry',
  'footprint',
  '__geometry_envelope',
]);

/** Max own properties shown inline per result row. */
export const PREVIEW_PROPERTIES = 4;

export interface ThingMatch {
  id: string;
  name: string;
  /** Relevance tier: 0 = exact, 1 = prefix, 2 = substring, 3 = ID match. Lower is better. */
  score: number;
  typeName?: string;
  ownPropertyCount: number;
  relationshipCount: number;
  /** Left unformatted: how a value should read depends on the type the platform declares for it,
   *  which this index does not carry. The page that shows them resolves that and formats. */
  previewProperties: Array<{ key: string; value: unknown }>;
}

export interface ThingSearchIndex {
  /** Maps subject ID → type name from the first "is" relationship found. */
  isSubjectToTypeName: Map<string, string>;
  /** Maps thing ID → number of relationships where it appears as subject or target. */
  relationshipCountByThing: Map<string, number>;
}

/**
 * Pre-compute indexes from model data so `searchThings` costs one pass over the Things.
 * Call once when `things` or `relationships` change (e.g., in a `useMemo`).
 */
export function buildThingSearchIndex(
  things: VosThing[],
  relationships: VosRelationship[],
): ThingSearchIndex {
  const thingMap = new Map(things.map((t) => [t.Id, t]));
  const isSubjectToTypeName = new Map<string, string>();
  const relationshipCountByThing = new Map<string, number>();

  for (const r of relationships) {
    relationshipCountByThing.set(r.SubjectId, (relationshipCountByThing.get(r.SubjectId) ?? 0) + 1);
    relationshipCountByThing.set(r.TargetId, (relationshipCountByThing.get(r.TargetId) ?? 0) + 1);

    const predicate = thingMap.get(r.PredicateId);
    if (predicate && predicate.Name.toLowerCase() === 'is') {
      if (!isSubjectToTypeName.has(r.SubjectId)) {
        const targetName = thingMap.get(r.TargetId)?.Name;
        if (targetName) isSubjectToTypeName.set(r.SubjectId, targetName);
      }
    }
  }

  return { isSubjectToTypeName, relationshipCountByThing };
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
    const previewProperties = ownKeys.slice(0, PREVIEW_PROPERTIES).map((key) => ({
      key,
      value: thing.Properties[key],
    }));

    matches.push({
      id: thing.Id,
      name: thing.Name,
      score,
      typeName: index.isSubjectToTypeName.get(thing.Id),
      ownPropertyCount: ownKeys.length,
      relationshipCount: index.relationshipCountByThing.get(thing.Id) ?? 0,
      previewProperties,
    });
  }

  matches.sort((a, b) => a.score - b.score || a.name.localeCompare(b.name));
  return matches;
}

/** Build a markdown table from a set of search results. */
export function buildThingSearchMarkdown(query: string, results: ThingMatch[]): string {
  const lines: string[] = [`# ${i18n.t('thingSearch.title')}: "${query}"`, ''];
  const headings = [
    i18n.t('thingSearch.markdown.name'),
    i18n.t('thingSearch.markdown.type'),
    i18n.t('thingSearch.markdown.properties'),
    i18n.t('thingSearch.markdown.relationships'),
  ];
  lines.push(`| ${headings.join(' | ')} |`, `|${headings.map(() => '---').join('|')}|`);
  for (const m of results) {
    lines.push(`| ${m.name} | ${m.typeName ?? '—'} | ${m.ownPropertyCount} | ${m.relationshipCount} |`);
  }
  return lines.join('\n');
}
