/**
 * The three readings of a kind, off the model the console already holds: what its instances
 * declare, which links they carry, and which states it derives. They are what lets a table be
 * composed from the model's own words rather than typed blind.
 */
import { IS_PREDICATE } from '../types/dashboard';
import type { ModelIndex } from './dashboardApi';
import { thingIdsOfArchetype, thingsOfArchetype } from './dashboardApi';
import type { ThingRangesResponse, InheritedRangeSetDto } from '../types/vos';
import { effectiveProperties } from '../utils/propertyMapper';

export interface PropertyCandidate {
  name: string;
  /** The kind that declares it — the kind asked about, or one above it. */
  declaredBy: string;
  /** A value read off one instance, so a reader can tell `capacity` on one kind from another. */
  example: unknown;
  numeric: boolean;
}

export interface EdgeCandidate {
  predicate: string;
  direction: 'out' | 'in';
  /** The kind at the far end, read off the instances actually reached. */
  reaches: string;
  /** How many links of this shape the instances carry. */
  count: number;
}

/** What the platform and the map keep on a Thing and no table has a column for. */
const NOT_OFFERED = new Set(['name_prefix', 'geometry', 'footprint', '__geometry_envelope']);

function isOffered(name: string): boolean {
  return !name.startsWith('__') && !NOT_OFFERED.has(name);
}

/** The archetype Thing a word names, or nothing where the word is not a kind. */
function kindNamed(kind: string, modelIndex: ModelIndex) {
  const thing = modelIndex.byName.get(kind);
  return thing && modelIndex.archetypeIds.has(thing.Id) ? thing : undefined;
}

/** A kind's properties, nearest declaration first: its own, then each kind above it up the `is`
 *  chain. A name declared twice is offered once, by the nearest kind. */
export function propertiesOf(kind: string, modelIndex: ModelIndex): PropertyCandidate[] {
  const start = kindNamed(kind, modelIndex);
  if (!start) return [];
  const example = thingsOfArchetype(kind, modelIndex)[0];
  const exampleValues = example ? effectiveProperties(example, modelIndex) : {};

  const offered: PropertyCandidate[] = [];
  const seen = new Set<string>();
  const visited = new Set<string>();
  const queue = [start];
  while (queue.length > 0) {
    const declaring = queue.shift()!;
    if (visited.has(declaring.Id)) continue;
    visited.add(declaring.Id);
    for (const [name, declared] of Object.entries(declaring.Properties)) {
      if (!isOffered(name) || seen.has(name)) continue;
      seen.add(name);
      const value = name in exampleValues ? exampleValues[name] : declared;
      offered.push({
        name,
        declaredBy: declaring.Name,
        example: value,
        numeric: typeof value === 'number' || typeof declared === 'number',
      });
    }
    for (const parentId of modelIndex.isParents.get(declaring.Id) ?? []) {
      const parent = modelIndex.byId.get(parentId);
      if (parent) queue.push(parent);
    }
  }
  return offered;
}

/** The links a kind's instances carry, grouped by predicate, direction and the kind reached, most
 *  common first. `is` types rather than relates, so it is never offered. */
export function edgesFrom(kind: string, modelIndex: ModelIndex): EdgeCandidate[] {
  if (!kindNamed(kind, modelIndex)) return [];
  const members = thingIdsOfArchetype(kind, modelIndex);
  const isId = modelIndex.predicateNameToId.get(IS_PREDICATE);
  const counted = new Map<string, EdgeCandidate>();
  const count = (predicate: string, direction: 'out' | 'in', otherId: string) => {
    for (const parentId of modelIndex.isParents.get(otherId) ?? []) {
      const reaches = modelIndex.byId.get(parentId)?.Name;
      if (!reaches) continue;
      const key = edgeKey({ predicate, direction, reaches });
      const found = counted.get(key);
      if (found) found.count += 1;
      else counted.set(key, { predicate, direction, reaches, count: 1 });
    }
  };
  for (const relationship of modelIndex.relationships) {
    if (relationship.PredicateId === isId) continue;
    const predicate = modelIndex.predicateIdToName.get(relationship.PredicateId);
    if (!predicate) continue;
    if (members.has(relationship.SubjectId)) count(predicate, 'out', relationship.TargetId);
    if (members.has(relationship.TargetId)) count(predicate, 'in', relationship.SubjectId);
  }
  return [...counted.values()].sort(
    (a, b) => b.count - a.count || a.predicate.localeCompare(b.predicate) || a.reaches.localeCompare(b.reaches),
  );
}

/** One word for a link candidate, so a list of them can be chosen from by value. */
export function edgeKey(edge: Pick<EdgeCandidate, 'predicate' | 'direction' | 'reaches'>): string {
  return `${edge.predicate}|${edge.direction}|${edge.reaches}`;
}

/** Every kind with at least one instance, in name order — a table of a kind nothing is cannot
 *  show a row, so an empty kind is not offered. */
export function kindsOffered(modelIndex: ModelIndex): string[] {
  const offered: string[] = [];
  for (const id of modelIndex.archetypeIds) {
    const kind = modelIndex.byId.get(id);
    if (kind && thingIdsOfArchetype(kind.Name, modelIndex).size > 0) offered.push(kind.Name);
  }
  return offered.sort((a, b) => a.localeCompare(b));
}

/** The states a kind derives — its own and every one inherited from the kinds above it — as the
 *  platform reports them for the archetype, once each and in name order. */
export function statesOf(ranges: Pick<ThingRangesResponse, 'OwnRanges' | 'InheritedRanges'>): string[] {
  const names = new Set<string>(ranges.OwnRanges.map((range) => range.Name));
  const walk = (sets: InheritedRangeSetDto[]) => {
    for (const set of sets) {
      for (const range of set.Ranges) names.add(range.Name);
      walk(set.Inherited ?? []);
    }
  };
  walk(ranges.InheritedRanges);
  return [...names].sort((a, b) => a.localeCompare(b));
}
