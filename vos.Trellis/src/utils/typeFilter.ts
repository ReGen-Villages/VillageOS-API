// A "type" is any Thing that some other Thing `is`-relates to; there is no
// fixed vocabulary. The synthetic "(no type)" bucket lets the panel's "None"
// action mean "show nothing → empty graph".

import type { VosThing, VosRelationship } from '../types/vos';

const IS_PREDICATE_NAME = 'is';

// Reserved sentinel — cannot collide with a real Thing id because real ids are GUIDs.
export const NO_TYPE_ID = '__noType__';

export const NO_TYPE_NAME = '(no type)';

export interface TypeStat {
  typeId: string;
  name: string;
  instanceCount: number;
}

/**
 * Bug #5363 — coalesced presentation: one row per Name regardless of how
 * many distinct type-Things share that Name. Thing Names are NOT unique in
 * the system (Name is just a display label; identity is the GUID), so an
 * IFC import can legitimately produce N type-Things named e.g.
 * "Solar_Panel-Tesla:Solar Panel". The user thinks of the row as one
 * category and wants one toggle for the lot.
 *
 * `typeIds` carries every underlying type-Thing id sharing this name so the
 * caller can toggle them together via the existing per-id hiddenTypeIds
 * machinery. The synthetic NO_TYPE bucket appears here as its own group of
 * one (typeIds = [NO_TYPE_ID]).
 */
export interface TypeGroupStat {
  /** Display name shared by every type-Thing in this group. */
  name: string;
  /** All type-Thing ids that share this Name. Length ≥ 1. */
  typeIds: string[];
  /** Sum of instance counts across every type-Thing in this group. */
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
 * Bug #5363 — collapse a TypeStat[] into one entry per Name, summing
 * counts and concatenating typeIds. Order: the new groups inherit the sort
 * position of their first member, so the heaviest single-typeId types stay
 * near the top. The synthetic NO_TYPE bucket remains last because its
 * single TypeStat already sorts last in `discoverTypes`.
 *
 * Pure function. The caller (TypeFilterPanel) renders one row per
 * TypeGroupStat and toggles all underlying typeIds together.
 */
export function groupTypesByName(types: readonly TypeStat[]): TypeGroupStat[] {
  // Preserve first-appearance order so the input's sort carries through.
  const order: string[] = [];
  const groups = new Map<string, TypeGroupStat>();
  for (const t of types) {
    let g = groups.get(t.name);
    if (!g) {
      g = { name: t.name, typeIds: [], instanceCount: 0 };
      groups.set(t.name, g);
      order.push(t.name);
    }
    g.typeIds.push(t.typeId);
    g.instanceCount += t.instanceCount;
  }
  return order.map((name) => groups.get(name)!);
}

/**
 * Feature #5386 — sort orderings exposed in the TypeFilterPanel selector.
 *   - count-desc  large buckets first (panel default, matches discoverTypes)
 *   - count-asc   smallest buckets first
 *   - name-asc    A → Z
 *   - name-desc   Z → A
 *
 * The synthetic NO_TYPE bucket always sorts to the END regardless of choice —
 * it's a special "everything else" category, not a real type, and shouldn't
 * displace meaningful rows when a user picks Name A→Z.
 */
export type SortOrder = 'count-desc' | 'count-asc' | 'name-asc' | 'name-desc';

export function sortTypeGroups(
  groups: readonly TypeGroupStat[],
  order: SortOrder,
): TypeGroupStat[] {
  const isNoType = (g: TypeGroupStat) => g.name === NO_TYPE_NAME;

  return [...groups].sort((a, b) => {
    // NO_TYPE sentinel always tail-sorts.
    if (isNoType(a) !== isNoType(b)) return isNoType(a) ? 1 : -1;

    switch (order) {
      case 'count-desc':
        return b.instanceCount - a.instanceCount || a.name.localeCompare(b.name);
      case 'count-asc':
        return a.instanceCount - b.instanceCount || a.name.localeCompare(b.name);
      case 'name-asc':
        return a.name.localeCompare(b.name);
      case 'name-desc':
        return b.name.localeCompare(a.name);
    }
  });
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
