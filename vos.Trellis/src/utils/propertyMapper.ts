import type { VosThing, VosRelationship, InheritedPropertySet } from '../types/vos';

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
  // The platform renamed the inherited-value field InheritedProperties -> InheritedOverrides; read either.
  const inherited = thing.InheritedProperties
    ?? (thing as unknown as { InheritedOverrides?: Record<string, InheritedPropertySet> }).InheritedOverrides;
  return {
    ...thing,
    Properties: unwrapProperties(thing.Properties),
    InheritedProperties: unwrapInheritedPropertySet(inherited),
  };
}

// Memoize the flattened result per Thing identity (Bug #5941). effectiveProperties is
// called per-Thing per-property across aggregate/compareEntities/stateList, and each call
// walks the whole ancestor chain and allocates a merged object. The store never mutates a
// Thing in place — setThings/upsertThing/applyThingPropertyUpdate all produce new objects —
// so a stale entry is impossible: a changed Thing is a new key, and the old key is GC'd.
const effectivePropertiesCache = new WeakMap<object, Readonly<Record<string, unknown>>>();

/**
 * A Thing's effective properties: own + inherited overrides, own winning, flattened and unwrapped.
 * Under lazy inheritance an instance's value for an inherited name is relocated out of Properties into
 * InheritedProperties, so reading Properties alone misses it. Call unwrapThing first (values raw here).
 *
 * Precedence is deterministic: own > nearer ancestor > farther ancestor, and among same-distance
 * sibling ancestors the one whose `SourceName` sorts last wins — so a property defined by two sibling
 * archetypes always resolves the same way regardless of server JSON key order. The returned object is
 * frozen (it is a shared cache entry); callers read or spread it but must not mutate it.
 */
export function effectiveProperties(
  thing: Pick<VosThing, 'Properties' | 'InheritedProperties'>,
): Readonly<Record<string, unknown>> {
  const cached = effectivePropertiesCache.get(thing);
  if (cached) return cached;

  const merged: Record<string, unknown> = {};
  const collect = (sets?: Record<string, InheritedPropertySet>): void => {
    if (!sets) return;
    // Fixed order (by SourceName) makes sibling resolution deterministic; assigning in
    // ascending order means the alphabetically-last sibling wins a same-distance conflict.
    const ordered = Object.values(sets).sort((a, b) => a.SourceName.localeCompare(b.SourceName));
    for (const set of ordered) {
      collect(set.Inherited as unknown as Record<string, InheritedPropertySet>);  // farther ancestors first
      Object.assign(merged, set.Properties);
    }
  };
  collect(thing.InheritedProperties);
  Object.assign(merged, thing.Properties);  // own wins

  const frozen = Object.freeze(merged);
  effectivePropertiesCache.set(thing, frozen);
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
