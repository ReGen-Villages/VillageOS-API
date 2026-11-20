// When an IFC instance and its type both define a name, such as ifcGlobalId or ifcClass, the instance's own
// value is stored as an override, so reading own properties alone misses it and the 3D viewer could not pick
// or filter what it drew. A value merely inherited is not read here; it needs the platform's resolved view
// (GET /api/things/{id}/properties).

import type { VosThing } from '@tendril-ui/tendril';

/** The Thing's value for `name` — its own, else the one it overrides. Null when it has neither. */
export function storedPropertyOf(thing: VosThing, name: string): unknown {
  const own = thing.Properties?.[name];
  if (own !== undefined && own !== null) return own;

  for (const set of Object.values(thing.InheritedOverrides ?? {})) {
    const overridden = set?.Properties?.[name];
    if (overridden !== undefined && overridden !== null) return overridden;
  }
  return null;
}

/** As `storedPropertyOf`, narrowed to a non-empty string. Null when absent or another type. */
export function storedTextOf(thing: VosThing, name: string): string | null {
  const value = storedPropertyOf(thing, name);
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/**
 * The Thing's IFC GlobalId. Null is ordinary rather than a fault: materials,
 * classifications and anything not rooted in the IFC have no identifier at all.
 */
export function ifcGlobalIdOf(thing: VosThing): string | null {
  return storedTextOf(thing, 'ifcGlobalId');
}
