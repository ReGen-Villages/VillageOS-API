import { DECLARED_WRITE_KINDS } from '../types/vos';
import type { DeclaredWriteKind, VosThing, VosRelationship, InheritedPropertySet } from '../types/vos';

/** A write kind the platform has a name for. What arrives is model data, so a value outside the set
 *  is left uninterpreted rather than passed on as a declaration nothing can read. */
function isDeclaredWriteKind(value: unknown): value is DeclaredWriteKind {
  return DECLARED_WRITE_KINDS.includes(value as DeclaredWriteKind);
}

/**
 * Unwrap a single typed property value.
 * The Mycelium serializes properties as { typeInfo: "vos.String", value: "..." }.
 * The GUI works with raw values, so we extract just the `value` field.
 */
export function unwrapPropertyValue(v: unknown): unknown {
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
  for (const [key, val] of Object.entries(props)) {
    if (val === null || typeof val !== 'object') continue;
    const declared = (val as Record<string, unknown>).writeKind;
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
  for (const [key, val] of Object.entries(props)) {
    result[key] = unwrapPropertyValue(val);
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
// *archetype* changed, which a Thing-only key (Bug #5941) cannot see (Bug #6048). Within a stable
// model the index identity holds, preserving the per-Thing memoization.
const effectivePropertiesCache = new WeakMap<object, WeakMap<object, Readonly<Record<string, unknown>>>>();

type ResolvableThing = { Id?: string; Properties: Record<string, unknown>; InheritedOverrides?: Record<string, InheritedPropertySet> };

/** Flatten an override tree onto `merged`, farther override-sources first, alphabetically-last SourceName winning. */
function collectOverrides(sets: Record<string, InheritedPropertySet> | undefined, merged: Record<string, unknown>): void {
  if (!sets) return;
  const ordered = Object.values(sets).sort((a, b) => a.SourceName.localeCompare(b.SourceName));
  for (const set of ordered) {
    collectOverrides(set.Inherited as unknown as Record<string, InheritedPropertySet>, merged);
    Object.assign(merged, set.Properties);
  }
}

/**
 * A Thing's effective properties: inherited defaults + stored overrides + own, own winning, flattened
 * and unwrapped. Resolution walks the `is`-chain via `lookup`: each ancestor archetype contributes its
 * OWN effective properties (its defaults and overrides already resolved), so inherited values the
 * instance never overrode are included — mirroring the server's resolve-on-read. Call unwrapThing first.
 *
 * Precedence is deterministic: own > override > nearer archetype > farther archetype; among same-distance
 * siblings the one whose `Name`/`SourceName` sorts last wins, so a value defined by two sibling archetypes
 * always resolves the same way regardless of key order. The result is frozen (a shared cache entry);
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

  // 1. Inherited defaults from ancestors — farthest first so a nearer archetype overwrites a farther
  //    one; each ancestor's own effective view is folded in, so its overrides and deeper defaults are
  //    already resolved. `visiting` guards against a malformed `is`-cycle.
  const parents = (thing.Id ? lookup.isParents.get(thing.Id) ?? [] : [])
    .map((id) => lookup.byId.get(id))
    .filter((p): p is VosThing => !!p && !visiting.has(p.Id))
    .sort((a, b) => a.Name.localeCompare(b.Name));
  if (parents.length && thing.Id) {
    visiting.add(thing.Id);
    for (const parent of parents) Object.assign(merged, resolveEffective(parent, lookup, visiting));
    visiting.delete(thing.Id);
  }

  // 2. This instance's stored overrides win over inherited defaults.
  collectOverrides(thing.InheritedOverrides, merged);

  // 3. Own properties win over everything.
  Object.assign(merged, thing.Properties);

  const frozen = Object.freeze(merged);
  perModel.set(thing, frozen);
  return frozen;
}

/**
 * Transform a VosRelationship from the API (with typed property wrappers)
 * into the GUI format (with raw property values).
 */
export function unwrapRelationship(rel: VosRelationship): VosRelationship {
  return {
    ...rel,
    Properties: unwrapProperties(rel.Properties),
  };
}
