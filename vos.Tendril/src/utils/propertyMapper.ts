import { DECLARED_WRITE_KINDS } from '../types/vos';
import { inPrecedenceOrder } from './precedenceOrder';
import type { DeclaredWriteKind, VosThing, VosRelationship, InheritedPropertySet, DerivedDefinition } from '../types/vos';

/** A write kind the platform has a name for. What arrives is model data, so a value outside the set
 *  is left uninterpreted rather than passed on as a declaration nothing can read. */
function isDeclaredWriteKind(value: unknown): value is DeclaredWriteKind {
  return DECLARED_WRITE_KINDS.includes(value as DeclaredWriteKind);
}

/**
 * Unwrap a single typed property value.
 * The Mycelium serializes properties as { typeInfo: "vos.String", value: "..." }.
 * A page works with raw values, so only the `value` field is kept.
 */
function unwrapPropertyValue(v: unknown): unknown {
  if (v !== null && typeof v === 'object' && 'value' in (v as Record<string, unknown>)) {
    return (v as Record<string, unknown>).value;
  }
  return v;
}

/**
 * The write kind each wrapped property was declared with, kept as the values are unwrapped.
 *
 * Read from the same envelope the value comes out of, so a Thing's declaration travels with it
 * rather than needing a second read per property. A property the platform declared nothing about
 * is left out rather than given a kind, which is the difference between the model saying a value
 * was sampled and the model saying nothing at all.
 */
function declaredWriteKinds(
  props: Record<string, unknown> | null | undefined,
): Record<string, DeclaredWriteKind> | undefined {
  if (!props) return undefined;
  const kinds: Record<string, DeclaredWriteKind> = {};
  for (const [key, value] of Object.entries(props)) {
    if (value === null || typeof value !== 'object') continue;
    const declared = (value as Record<string, unknown>).writeKind;
    if (isDeclaredWriteKind(declared)) kinds[key] = declared;
  }
  return Object.keys(kinds).length ? kinds : undefined;
}

/**
 * Unwrap all typed property values in a Properties record.
 */
export function unwrapProperties(
  props: Record<string, unknown> | null | undefined,
): Record<string, unknown> {
  if (!props) return {};
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(props)) {
    result[key] = unwrapPropertyValue(value);
  }
  return result;
}

/**
 * Recursively unwrap inherited property sets.
 */
function unwrapInheritedPropertySet(
  ips: Record<string, InheritedPropertySet> | null | undefined,
): Record<string, InheritedPropertySet> | undefined {
  if (!ips) return undefined;
  const result: Record<string, InheritedPropertySet> = {};
  for (const [key, set] of Object.entries(ips)) {
    result[key] = {
      ...set,
      Properties: unwrapProperties(set.Properties),
      PropertyWriteKinds: declaredWriteKinds(set.Properties),
      Inherited: unwrapInheritedPropertySet(set.Inherited as unknown as Record<string, InheritedPropertySet>) as unknown as Record<string, InheritedPropertySet>,
    };
  }
  return result;
}

/**
 * Transform a VosThing from the API (with typed property wrappers)
 * into the GUI format (with raw property values).
 */
export function unwrapThing(thing: VosThing): VosThing {
  return {
    ...thing,
    Properties: unwrapProperties(thing.Properties),
    PropertyWriteKinds: declaredWriteKinds(thing.Properties),
    InheritedOverrides: unwrapInheritedPropertySet(thing.InheritedOverrides),
  };
}

/** Minimal is-chain lookup effectiveProperties needs to resolve inherited defaults up the `is`-chain. */
export interface IsChainLookup {
  byId: Map<string, VosThing>;
  /** Thing id → the ids of the archetypes it is directly `is`-linked to. */
  isParents: Map<string, string[]>;
}

// Memoize per (model-index identity, Thing identity). The index is rebuilt whenever the model
// changes (buildModelIndex is memoized on things+relationships), so a fresh index correctly
// invalidates every entry — including instances whose inherited defaults changed because an
// *archetype* changed, which a Thing-only key cannot see. Within a stable
// model the index identity holds, preserving the per-Thing memoization.
const effectivePropertiesCache = new WeakMap<object, WeakMap<object, Readonly<Record<string, unknown>>>>();

type ResolvableThing = { Id?: string; Properties: Record<string, unknown>; InheritedOverrides?: Record<string, InheritedPropertySet> };

/** Flatten an override tree onto `merged`, farther override-sources first, the SourceName last in precedence order winning. */
function collectOverrides(sets: Record<string, InheritedPropertySet> | undefined, merged: Record<string, unknown>): void {
  if (!sets) return;
  const ordered = Object.values(sets).sort((a, b) => inPrecedenceOrder(a.SourceName, b.SourceName));
  for (const set of ordered) {
    collectOverrides(set.Inherited as unknown as Record<string, InheritedPropertySet>, merged);
    Object.assign(merged, set.Properties);
  }
}

/**
 * What a Thing states for itself: its own properties and its override store, own winning, and never the
 * `is` chain. A value written for a name the archetype declares is kept in the override store rather than
 * among the own properties, so a reader of own properties alone finds every such value absent — a saved
 * port read as an input named after its Thing. Distinct from effectiveProperties, which resolves the chain
 * and so would hand a member every default its archetype carries, marks included. Call unwrapThing first.
 */
export function statedProperties(thing: ResolvableThing): Record<string, unknown> {
  const stated: Record<string, unknown> = {};
  collectOverrides(thing.InheritedOverrides, stated);
  Object.assign(stated, thing.Properties);
  return stated;
}

/**
 * A Thing's effective properties: inherited defaults + stored overrides + own, own winning, flattened
 * and unwrapped. Resolution walks the `is`-chain via `lookup`: each ancestor archetype contributes its
 * OWN effective properties (its defaults and overrides already resolved), so inherited values the
 * instance never overrode are included. Call unwrapThing first.
 *
 * Precedence is own > override > nearer archetype > farther archetype. A name the Thing does not hold
 * itself and that two or more sources declare is left out, as the platform reads it as ambiguous and
 * computes nothing from it (see {@link readsAsAmbiguous}). Where one source reaches the Thing by two
 * routes that each state a value, the one whose `Name`/`SourceName` is last in precedence order wins,
 * so it resolves the same way regardless of key order. The result is frozen (a shared cache entry);
 * callers read or spread it but must not mutate it.
 */
export function effectiveProperties(
  thing: ResolvableThing,
  lookup: IsChainLookup,
): Readonly<Record<string, unknown>> {
  return resolveEffective(thing, lookup, new Set());
}

function resolveEffective(
  thing: ResolvableThing,
  lookup: IsChainLookup,
  visiting: Set<string>,
): Readonly<Record<string, unknown>> {
  let perModel = effectivePropertiesCache.get(lookup);
  if (!perModel) { perModel = new WeakMap(); effectivePropertiesCache.set(lookup, perModel); }
  const cached = perModel.get(thing);
  if (cached) return cached;

  const merged: Record<string, unknown> = {};

  // Ancestors fold in farthest first so a nearer archetype overwrites a farther one, each through its
  // own effective view so its overrides and deeper defaults are already resolved; then this instance's
  // stored overrides, then its own properties, each layer winning over the one before.
  foldInAncestors(thing, lookup, visiting, (parent) =>
    Object.assign(merged, resolveEffective(parent, lookup, visiting)),
  );
  collectOverrides(thing.InheritedOverrides, merged);
  Object.assign(merged, thing.Properties);

  const sources = inheritedSources(thing, lookup, visiting);
  for (const name of Object.keys(merged)) {
    if (!holdsOwn(thing, name) && (sources.get(name)?.size ?? 0) > 1) delete merged[name];
  }

  const frozen = Object.freeze(merged);
  perModel.set(thing, frozen);
  return frozen;
}

/**
 * Whether the platform reads `name` on this Thing as ambiguous: the Thing holds no property of that
 * name itself, and two or more sources declare it. A source is the Thing that first declared the name;
 * a kind stating its own value over the kind above it, or a Thing storing a value for a name it
 * inherits, is held against that source and adds none. Two routes to one source are one source.
 */
export function readsAsAmbiguous(thing: ResolvableThing, name: string, lookup: IsChainLookup): boolean {
  return !holdsOwn(thing, name) && (inheritedSources(thing, lookup, new Set()).get(name)?.size ?? 0) > 1;
}

function holdsOwn(thing: ResolvableThing, name: string): boolean {
  return !!thing.Properties && Object.hasOwn(thing.Properties, name);
}

type SourcesByName = Map<string, Set<string>>;

const passedOnCache = new WeakMap<object, WeakMap<object, SourcesByName>>();

/** The sources of each name a Thing inherits: those its archetypes pass on, and those of the values it stored. */
function inheritedSources(thing: ResolvableThing, lookup: IsChainLookup, visiting: Set<string>): SourcesByName {
  const sources: SourcesByName = new Map();
  foldInAncestors(thing, lookup, visiting, (parent) => addSources(sources, passedOn(parent, lookup, visiting)));
  addStoredSources(thing.InheritedOverrides, sources);
  return sources;
}

/** The sources of each name a member inherits through this Thing. A name the Thing declares that none of its
 *  own sources does makes the Thing that name's source; any other value it holds stands over a source above it. */
function passedOn(thing: VosThing, lookup: IsChainLookup, visiting: Set<string>): SourcesByName {
  let perModel = passedOnCache.get(lookup);
  if (!perModel) { perModel = new WeakMap(); passedOnCache.set(lookup, perModel); }
  const cached = perModel.get(thing);
  if (cached) return cached;

  const sources = inheritedSources(thing, lookup, visiting);
  for (const name of Object.keys(thing.Properties ?? {})) {
    if (!sources.has(name)) sources.set(name, new Set([thing.Id]));
  }
  perModel.set(thing, sources);
  return sources;
}

/** As {@link passedOn}, for the sets a Thing stored values in: a set nested inside another is a source
 *  farther up the same route, so a name both hold stands over the farther one and adds no source. */
function addStoredSources(sets: Record<string, InheritedPropertySet> | undefined, sources: SourcesByName): void {
  for (const set of Object.values(sets ?? {})) {
    const farther: SourcesByName = new Map();
    addStoredSources(set.Inherited, farther);
    for (const name of Object.keys(set.Properties ?? {})) {
      if (!farther.has(name)) addSource(farther, name, set.SourceId);
    }
    addSources(sources, farther);
  }
}

function addSources(into: SourcesByName, from: SourcesByName): void {
  for (const [name, ids] of from) for (const id of ids) addSource(into, name, id);
}

function addSource(into: SourcesByName, name: string, id: string): void {
  const held = into.get(name);
  if (held) held.add(id);
  else into.set(name, new Set([id]));
}

/**
 * Fold each archetype a Thing `is` into a merged view, farthest ancestor first so a nearer one
 * overwrites a farther one, and same-distance siblings in `Name` order so two declaring the same key
 * always resolve the same way. `visiting` guards against a malformed `is`-cycle.
 */
function foldInAncestors(
  thing: { Id?: string },
  lookup: IsChainLookup,
  visiting: Set<string>,
  fold: (parent: VosThing) => void,
): void {
  const parents = (thing.Id ? lookup.isParents.get(thing.Id) ?? [] : [])
    .map((id) => lookup.byId.get(id))
    .filter((p): p is VosThing => !!p && !visiting.has(p.Id))
    .sort((a, b) => inPrecedenceOrder(a.Name, b.Name));
  if (!parents.length || !thing.Id) return;
  visiting.add(thing.Id);
  for (const parent of parents) fold(parent);
  visiting.delete(thing.Id);
}

/**
 * The derived definitions a Thing resolves: its own, plus those of the archetypes it `is`, nearer
 * winning. The same `is` walk {@link effectiveProperties} makes, because a definition is declared once
 * on the Thing that owns it and every member resolves it through that chain — a member computing its
 * own value carries the value and not the formula. There is no override step: a definition is declared
 * or inherited, and nothing stores one against a Thing that did not declare it.
 *
 * Not memoized. A page asks this for the few figures it shows the working of, where
 * {@link effectiveProperties} is asked for every binding on every refresh.
 */
export function effectiveDerivedDefinitions(
  thing: { Id?: string; DerivedProperties?: Record<string, DerivedDefinition> },
  lookup: IsChainLookup,
): Readonly<Record<string, DerivedDefinition>> {
  return resolveDefinitions(thing, lookup, new Set());
}

function resolveDefinitions(
  thing: { Id?: string; DerivedProperties?: Record<string, DerivedDefinition> },
  lookup: IsChainLookup,
  visiting: Set<string>,
): Readonly<Record<string, DerivedDefinition>> {
  const merged: Record<string, DerivedDefinition> = {};
  foldInAncestors(thing, lookup, visiting, (parent) =>
    Object.assign(merged, resolveDefinitions(parent, lookup, visiting)),
  );
  Object.assign(merged, thing.DerivedProperties);
  return merged;
}

/**
 * Transform a VosRelationship from the API (with typed property wrappers)
 * into the GUI format (with raw property values).
 */
export function unwrapRelationship(relationship: VosRelationship): VosRelationship {
  return {
    ...relationship,
    Properties: unwrapProperties(relationship.Properties),
    PropertyWriteKinds: declaredWriteKinds(relationship.Properties),
  };
}
