/**
 * Where a value came from, read off the model rather than off the property's name.
 *
 * The model draws the distinction already: what a person asserted is declared a different kind of
 * write from what was sampled or fetched, and a value the Thing never wrote is one its archetype
 * supplies to every member. This turns those declarations into the answers a reader needs, and
 * gives no answer where the model gives none — an input with nothing recorded about it must read as
 * unrecorded, never as a measurement.
 */
import type { DeclaredWriteKind, InheritedPropertySet, ValueOrigin, VosThing } from '../types/vos';
import { readsAsAmbiguous, type IsChainLookup } from './propertyMapper';
import { inPrecedenceOrder } from './precedenceOrder';

export function valueOrigin(thing: VosThing, property: string, lookup: IsChainLookup): ValueOrigin {
  if (readsAsAmbiguous(thing, property, lookup)) return { origin: 'ambiguous', assumedFrom: null };
  const source = statedBy(thing, property, lookup, new Set());
  if (!source) return { origin: 'unknown', assumedFrom: null };
  if (source !== thing) return { origin: 'assumed', assumedFrom: source.Name };
  switch (declaredWriteKind(thing, property, lookup, new Set())) {
    case 'FactOnly': return { origin: 'stated', assumedFrom: null };
    case 'ObservationOnly': return { origin: 'measured', assumedFrom: null };
    default: return { origin: 'unknown', assumedFrom: null };
  }
}

/** The write kind the platform judges a write to this property by: that of the property the Thing holds
 *  under the name, or else of the nearest archetype holding it, valued or not. */
export function resolvedWriteKind(thing: VosThing, property: string, lookup: IsChainLookup): DeclaredWriteKind | undefined {
  const holder = holderOfName(thing, property, lookup, new Set());
  return holder ? storedWriteKind(holder, property) : undefined;
}

function holderOfName(
  thing: VosThing,
  property: string,
  lookup: IsChainLookup,
  visiting: Set<string>,
): VosThing | null {
  if (holdsName(thing, property)) return thing;
  if (visiting.has(thing.Id)) return null;
  visiting.add(thing.Id);
  for (const parent of ancestorsInPrecedenceOrder(thing, lookup)) {
    const holder = holderOfName(parent, property, lookup, visiting);
    if (holder) return holder;
  }
  return null;
}

function holdsName(thing: VosThing, property: string): boolean {
  return (
    Object.hasOwn(thing.Properties ?? {}, property) ||
    firstInStoredSets(thing.InheritedOverrides, (set) => (Object.hasOwn(set.Properties ?? {}, property) ? true : undefined)) === true
  );
}

/** The Thing whose value this one resolves to: itself where it holds one, otherwise the nearest
 *  archetype up the `is`-chain that holds one. A malformed model can put a cycle in that chain, and
 *  a walk over model-supplied data that trusts it is a stack overflow, so each is visited once. */
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
    const supplied = statedBy(parent, property, lookup, visiting);
    if (supplied) return supplied;
  }
  return null;
}

/** Where the value is stored says nothing about where its kind is declared: a Thing can hold a value
 *  under a name only its archetype describes. So the declaration is looked for separately, and a
 *  property nothing declares answers with nothing — which is what keeps silence distinguishable
 *  from a declaration that the value was sampled. */
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
 * siblings in precedence order and lets each overwrite the last, so the last one in that order
 * holds the value and is asked here first. Siblings that hold the name from two sources never get
 * here, as the name then reads as ambiguous; the order decides only between routes to one source.
 */
function ancestorsInPrecedenceOrder(thing: VosThing, lookup: IsChainLookup): VosThing[] {
  return (lookup.isParents.get(thing.Id) ?? [])
    .map((id) => lookup.byId.get(id))
    .filter((parent): parent is VosThing => !!parent)
    .sort((a, b) => inPrecedenceOrder(b.Name, a.Name));
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
