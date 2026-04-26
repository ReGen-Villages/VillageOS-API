// Feature #5362 — discover the set of "type Things" in a loaded model and
// count how many instances each one has.
//
// Domain-agnostic by design. The system has no fixed vocabulary of types —
// a "type" is simply any Thing that some other Thing `is`-relates to. This
// matches the convention the existing graphologyMapper.buildRelationshipIndex
// already uses to render type/instance roles, but exposed as a reusable utility
// so the type-filter UI on the Graph page and the Model page can both consume it.

import type { VosThing, VosRelationship } from '../types/vos';

const IS_PREDICATE_NAME = 'is';

export interface TypeStat {
  /** Thing id of the type Thing (the target of one or more `is` relationships). */
  typeId: string;
  /** Display name (the type Thing's Name). */
  name: string;
  /** Number of distinct subject Things that `is`-relate to this type. */
  instanceCount: number;
}

/**
 * Walk Things + Relationships, group every `is` relationship by its target,
 * and return one TypeStat per distinct type Thing. Sorted by descending
 * instance count then by name so the panel shows the heaviest types first.
 *
 * Performance: O(N + M) — single pass over relationships plus a Map lookup.
 */
export function discoverTypes(
  things: VosThing[],
  relationships: VosRelationship[],
): TypeStat[] {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  // typeId -> Set<subjectId> so duplicate `is` relationships from the same
  // subject only count once. (IFC models can emit multiple `is` rels for the
  // same instance via Tier 2 type-info pathways.)
  const typeToInstances = new Map<string, Set<string>>();

  for (const rel of relationships) {
    const predName = thingNames.get(rel.PredicateId);
    if (predName?.toLowerCase() !== IS_PREDICATE_NAME) continue;
    let set = typeToInstances.get(rel.TargetId);
    if (!set) {
      set = new Set<string>();
      typeToInstances.set(rel.TargetId, set);
    }
    set.add(rel.SubjectId);
  }

  const out: TypeStat[] = [];
  for (const [typeId, instanceSet] of typeToInstances) {
    const name = thingNames.get(typeId);
    if (!name) continue; // dangling reference — skip
    out.push({ typeId, name, instanceCount: instanceSet.size });
  }
  out.sort((a, b) => b.instanceCount - a.instanceCount || a.name.localeCompare(b.name));
  return out;
}

/**
 * For each instance Thing, return the id of the type it `is`-relates to (or null).
 * If an instance has multiple `is` relationships, the first one wins — same
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
 * has hidden `hiddenTypeIds`. An instance is hidden if its type is in the set;
 * a relationship is hidden if either endpoint is hidden OR if the relationship
 * is itself an `is` link to a hidden type Thing.
 *
 * The hidden type Things themselves stay visible — they're often hubs in the
 * graph and removing them confuses orientation. Hide individual instances, not
 * the labels.
 *
 * Pure function; the new arrays preserve original order. Returns the unfiltered
 * inputs (same references) when hiddenTypeIds is empty so React memoization
 * upstream doesn't pay a copy cost on the hot path.
 */
export function applyTypeFilter(
  things: VosThing[],
  relationships: VosRelationship[],
  hiddenTypeIds: ReadonlySet<string>,
): { things: VosThing[]; relationships: VosRelationship[] } {
  if (hiddenTypeIds.size === 0) return { things, relationships };

  const instanceTypeIndex = buildInstanceTypeIndex(things, relationships);

  const hiddenInstanceIds = new Set<string>();
  for (const [instanceId, typeId] of instanceTypeIndex) {
    if (hiddenTypeIds.has(typeId)) hiddenInstanceIds.add(instanceId);
  }

  const filteredThings = things.filter((t) => !hiddenInstanceIds.has(t.Id));
  const filteredRels = relationships.filter(
    (r) => !hiddenInstanceIds.has(r.SubjectId) && !hiddenInstanceIds.has(r.TargetId),
  );

  return { things: filteredThings, relationships: filteredRels };
}
