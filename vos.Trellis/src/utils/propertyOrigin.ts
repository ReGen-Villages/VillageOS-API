/**
 * Where a value came from, read off the model rather than off the property's name.
 *
 * The model already draws the distinction and nothing showed it: what a person asserted is declared
 * a different kind of write from what was sampled or fetched, and a value the Thing never wrote is
 * one its archetype supplies to every member. This turns those declarations into the four answers a
 * reader needs, and gives no answer where the model gives none — an input with nothing recorded
 * about it reads as unrecorded, never as a measurement.
 */
import type { DeclaredWriteKind, InheritedPropertySet, ValueOrigin, VosThing } from '../types/vos';
import { effectiveProperties, type IsChainLookup } from './propertyMapper';

/** Where a Thing's value for one property came from. */
export function valueOrigin(thing: VosThing, property: string, lookup: IsChainLookup): ValueOrigin {
  const source = statedBy(thing, property, lookup, new Set());
  if (!source) return { origin: 'unknown', assumedFrom: null };
  if (source !== thing) return { origin: 'assumed', assumedFrom: source.Name };
  switch (declaredWriteKind(thing, property, lookup, new Set())) {
    case 'FactOnly': return { origin: 'stated', assumedFrom: null };
    case 'ObservationOnly': return { origin: 'measured', assumedFrom: null };
    default: return { origin: 'unknown', assumedFrom: null };
  }
}

/**
 * The Thing whose value this one resolves to: itself where it holds one, otherwise the archetype up
 * the `is`-chain that supplies it, or none.
 *
 * Descends only into an archetype whose own effective value is not null, so the walk follows the
 * same value {@link effectiveProperties} resolves rather than an unrelated one further up.
 */
function statedBy(
  thing: VosThing,
  property: string,
  lookup: IsChainLookup,
  visiting: Set<string>,
): VosThing | null {
  if (storedValue(thing, property) != null) return thing;
  if (visiting.has(thing.Id)) return null;
  visiting.add(thing.Id);
  for (const parent of ancestorsInPrecedenceOrder(thing, lookup)) {
    if (effectiveProperties(parent, lookup)[property] == null) continue;
    const supplied = statedBy(parent, property, lookup, visiting);
    if (supplied) return supplied;
  }
  return null;
}

/**
 * The write kind the model declares for a property: on the Thing itself, or — where a stored value
 * carries none — on the nearest archetype that declares one.
 *
 * A property nothing declares answers with nothing, which is what keeps silence distinguishable
 * from a declaration that the value was sampled.
 */
function declaredWriteKind(
  thing: VosThing,
  property: string,
  lookup: IsChainLookup,
  visiting: Set<string>,
): DeclaredWriteKind | undefined {
  const here = storedWriteKind(thing, property);
  if (here) return here;
  if (visiting.has(thing.Id)) return undefined;
  visiting.add(thing.Id);
  for (const parent of ancestorsInPrecedenceOrder(thing, lookup)) {
    const declared = declaredWriteKind(parent, property, lookup, visiting);
    if (declared) return declared;
  }
  return undefined;
}

/**
 * The archetypes a Thing is, in the order {@link effectiveProperties} lets them win: it merges
 * siblings in ascending name order and lets each overwrite the last, so the alphabetically last one
 * holds the value and is asked here first.
 */
function ancestorsInPrecedenceOrder(thing: VosThing, lookup: IsChainLookup): VosThing[] {
  return (lookup.isParents.get(thing.Id) ?? [])
    .map((id) => lookup.byId.get(id))
    .filter((parent): parent is VosThing => !!parent)
    .sort((a, b) => b.Name.localeCompare(a.Name));
}

/** A value the Thing holds itself — its own, or one it stored under a name its archetype declares,
 *  which is where the platform relocates a write onto an inherited name. */
function storedValue(thing: VosThing, property: string): unknown {
  const own = thing.Properties?.[property];
  if (own != null) return own;
  return firstInStoredSets(thing.InheritedOverrides, (set) => set.Properties?.[property]);
}

function storedWriteKind(thing: VosThing, property: string): DeclaredWriteKind | undefined {
  return (
    thing.PropertyWriteKinds?.[property] ??
    firstInStoredSets(thing.InheritedOverrides, (set) => set.PropertyWriteKinds?.[property])
  );
}

/** The first answer any of a Thing's stored override sets gives, nested ones included. Their order
 *  cannot change the answer: an override set holds what this Thing stored, whichever source it
 *  stored it under. */
function firstInStoredSets<T>(
  sets: Record<string, InheritedPropertySet> | undefined,
  read: (set: InheritedPropertySet) => T | null | undefined,
): T | undefined {
  for (const set of Object.values(sets ?? {})) {
    const answer = read(set);
    if (answer != null) return answer;
    const nested = firstInStoredSets(set.Inherited, read);
    if (nested != null) return nested;
  }
  return undefined;
}
