// A type is any Thing another Thing `is`; there is no fixed list. The no-type group lets the panel's "None"
// empty the graph, and the panel shows its name in the reader's language.

import type { VosThing, VosRelationship } from '@tendril-ui/tendril';

const IS_PREDICATE_NAME = 'is';

// No real Thing identifier has this form.
export const NO_TYPE_ID = '__noType__';

export interface TypeStat {
  typeId: string;
  name: string;
  instanceCount: number;
}

/** One row per name, because Thing names are not unique: an IFC import can make several type Things with one
 *  name, and a person thinks of them as one category with one switch. */
export interface TypeGroupStat {
  name: string;
  /** Every type Thing with this name, switched together; at least one. */
  typeIds: string[];
  instanceCount: number;
}

/** Each Thing falls in exactly one group, its first `is` target or the no-type group, so the counts add up to
 *  the number of Things, which the panel header relies on. One pass with a map lookup, so the cost grows with
 *  the model and not with its square. */
export function discoverTypes(
  things: VosThing[],
  relationships: VosRelationship[],
): TypeStat[] {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const instanceTypeIndex = buildInstanceTypeIndex(things, relationships);

  const buckets = new Map<string, Set<string>>();

  // A type Thing counts in its own group, so "None" hides it too.
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
    const name = typeId === NO_TYPE_ID ? NO_TYPE_ID : thingNames.get(typeId);
    if (!name) continue;
    out.push({ typeId, name, instanceCount: members.size });
  }
  // The no-type group sorts last, so it does not push real types down however large it is.
  out.sort((a, b) => {
    if (a.typeId === NO_TYPE_ID) return 1;
    if (b.typeId === NO_TYPE_ID) return -1;
    return b.instanceCount - a.instanceCount || a.name.localeCompare(b.name);
  });
  return out;
}

/** A group keeps the place of its first member, so the heaviest types stay near the top and the no-type
 *  group stays last. */
export function groupTypesByName(types: readonly TypeStat[]): TypeGroupStat[] {
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

/** Whatever the order, the no-type group sorts last, because it is not a real type and should not push real
 *  rows down. */
export type SortOrder = 'count-desc' | 'count-asc' | 'name-asc' | 'name-desc';

export function sortTypeGroups(
  groups: readonly TypeGroupStat[],
  order: SortOrder,
): TypeGroupStat[] {
  const isNoType = (g: TypeGroupStat) => g.name === NO_TYPE_ID;

  return [...groups].sort((a, b) => {
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

/** The first `is` wins, as it does in graphologyMapper. */
export function buildInstanceTypeIndex(
  things: VosThing[],
  relationships: VosRelationship[],
): Map<string, string> {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const subjectToType = new Map<string, string>();
  for (const relationship of relationships) {
    const predicateName = thingNames.get(relationship.PredicateId);
    if (predicateName?.toLowerCase() !== IS_PREDICATE_NAME) continue;
    if (!subjectToType.has(relationship.SubjectId)) {
      subjectToType.set(relationship.SubjectId, relationship.TargetId);
    }
  }
  return subjectToType;
}

/** A hidden type's Things go, Things with no type go when the no-type group is hidden, and a hidden type Thing
 *  goes too, so "None" empties the graph; a relationship goes with either end. With nothing hidden the inputs
 *  come back as they are, so the memoised results above them are not copied on every render. */
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
      hiddenThingIds.add(t.Id);
    } else if (!typeId && noTypeHidden) {
      hiddenThingIds.add(t.Id);
    } else if (hiddenTypeIds.has(t.Id)) {
      hiddenThingIds.add(t.Id);
    }
  }

  const filteredThings = things.filter((t) => !hiddenThingIds.has(t.Id));
  const filteredRelationships = relationships.filter(
    (r) => !hiddenThingIds.has(r.SubjectId) && !hiddenThingIds.has(r.TargetId),
  );

  return { things: filteredThings, relationships: filteredRelationships };
}
