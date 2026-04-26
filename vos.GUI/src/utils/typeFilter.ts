// Feature #5362 — discover the set of "type Things" in a loaded model and
// count how many Things belong to each, plus a synthetic "(no type)" bucket
// for Things without an `is` relation. The bucket-of-everything-else makes
// the filter panel match the user's mental model: "None" means "show
// nothing" — empty graph.
//
// Domain-agnostic by design. The system has no fixed vocabulary of types —
// a "type" is simply any Thing that some other Thing `is`-relates to.

import type { VosThing, VosRelationship } from '../types/vos';

const IS_PREDICATE_NAME = 'is';

/**
 * Synthetic typeId for the "(no type)" bucket — Things in the graph that
 * have no `is` relationship to any type (predicates, the GUI_Settings
 * instance, any Thing the user created directly without classifying it).
 * Treated as a first-class entry in the type-filter panel so users can hide
 * them too: the panel's "None" action then matches "show nothing → empty
 * graph".
 *
 * Reserved sentinel — guaranteed not to collide with any real Thing id
 * because real ids are GUIDs.
 */
export const NO_TYPE_ID = '__noType__';

/** Display name shown in the panel for the synthetic bucket. */
export const NO_TYPE_NAME = '(no type)';

export interface TypeStat {
  /** Thing id of the type Thing, or NO_TYPE_ID for the synthetic bucket. */
  typeId: string;
  /** Display name (the type Thing's Name, or NO_TYPE_NAME). */
  name: string;
  /** Number of Things in this bucket. */
  instanceCount: number;
}

/**
 * Walk Things + Relationships and group by `is`-target. Returns one TypeStat
 * per distinct type Thing PLUS one entry under NO_TYPE_ID counting Things
 * that have no `is` relation. Sorted by descending count then by name so the
 * panel surfaces the heaviest buckets first; the synthetic bucket sorts
 * naturally with the rest.
 *
 * Every Thing in `things` falls into exactly one bucket (its first-seen
 * `is` target, or NO_TYPE_ID). Sum of all `instanceCount`s equals
 * `things.length` — invariant the panel header relies on.
 *
 * Performance: O(N + M) — single pass over relationships plus a Map lookup.
 */
export function discoverTypes(
  things: VosThing[],
  relationships: VosRelationship[],
): TypeStat[] {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const instanceTypeIndex = buildInstanceTypeIndex(things, relationships);

  // typeId -> Set<thingId>. typeId is either a real type Thing id or
  // NO_TYPE_ID for Things without a registered `is` target.
  const buckets = new Map<string, Set<string>>();

  // Type Things themselves count in their own bucket — selecting "None" must
  // hide them too. So first, seed each type Thing into its own bucket.
  for (const t of things) {
    const typeId = instanceTypeIndex.get(t.Id);
    const bucketKey = typeId && thingNames.has(typeId) ? typeId : NO_TYPE_ID;
    let set = buckets.get(bucketKey);
    if (!set) {
      set = new Set<string>();
      buckets.set(bucketKey, set);
    }
    set.add(t.Id);
  }

  const out: TypeStat[] = [];
  for (const [typeId, members] of buckets) {
    const name = typeId === NO_TYPE_ID ? NO_TYPE_NAME : thingNames.get(typeId);
    if (!name) continue;
    out.push({ typeId, name, instanceCount: members.size });
  }
  // Real types sort by count desc then name asc; the synthetic no-type bucket
  // always sorts to the end so it doesn't displace meaningful types in the
  // panel even when its count is high.
  out.sort((a, b) => {
    if (a.typeId === NO_TYPE_ID) return 1;
    if (b.typeId === NO_TYPE_ID) return -1;
    return b.instanceCount - a.instanceCount || a.name.localeCompare(b.name);
  });
  return out;
}

/**
 * For each Thing, return the id of the type it `is`-relates to (or null).
 * If a Thing has multiple `is` relationships, the first one wins — same
 * behavior as graphologyMapper.buildRelationshipIndex's isSubjectToTypeName.
 */
export function buildInstanceTypeIndex(
  things: VosThing[],
  relationships: VosRelationship[],
): Map<string, string> {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const subjectToType = new Map<string, string>();
  for (const rel of relationships) {
    const predName = thingNames.get(rel.PredicateId);
    if (predName?.toLowerCase() !== IS_PREDICATE_NAME) continue;
    if (!subjectToType.has(rel.SubjectId)) {
      subjectToType.set(rel.SubjectId, rel.TargetId);
    }
  }
  return subjectToType;
}

/**
 * Filter Things + Relationships down to what should render after the user
 * has hidden `hiddenTypeIds`. A Thing is hidden when:
 *
 *   - its type-Thing id is in `hiddenTypeIds` (instances), OR
 *   - it has no `is` target AND NO_TYPE_ID is in `hiddenTypeIds`
 *     (predicates, GUI_Settings, any unclassified Thing), OR
 *   - the Thing is itself a hidden type Thing (so the type "label" hub
 *     vanishes alongside its instances — selecting "None" yields an
 *     empty graph).
 *
 * A relationship is dropped when either endpoint is hidden.
 *
 * Pure function; new arrays preserve original order. Returns the unfiltered
 * inputs (same references) when hiddenTypeIds is empty so upstream React
 * memoization doesn't pay a copy cost on the hot path.
 */
export function applyTypeFilter(
  things: VosThing[],
  relationships: VosRelationship[],
  hiddenTypeIds: ReadonlySet<string>,
): { things: VosThing[]; relationships: VosRelationship[] } {
  if (hiddenTypeIds.size === 0) return { things, relationships };

  const instanceTypeIndex = buildInstanceTypeIndex(things, relationships);
  const noTypeHidden = hiddenTypeIds.has(NO_TYPE_ID);

  const hiddenThingIds = new Set<string>();
  for (const t of things) {
    const typeId = instanceTypeIndex.get(t.Id);
    if (typeId && hiddenTypeIds.has(typeId)) {
      // Instance of a hidden type
      hiddenThingIds.add(t.Id);
    } else if (!typeId && noTypeHidden) {
      // Has no `is` target AND the synthetic bucket is hidden
      hiddenThingIds.add(t.Id);
    } else if (hiddenTypeIds.has(t.Id)) {
      // The Thing IS itself a hidden type Thing
      hiddenThingIds.add(t.Id);
    }
  }

  const filteredThings = things.filter((t) => !hiddenThingIds.has(t.Id));
  const filteredRels = relationships.filter(
    (r) => !hiddenThingIds.has(r.SubjectId) && !hiddenThingIds.has(r.TargetId),
  );

  return { things: filteredThings, relationships: filteredRels };
}
