// Reading a Thing's property wherever it is stored.
//
// Bug #6191: when an IFC instance and the type it `is`-relates to both define a
// name, the instance's own value is stored as an override rather than as an own
// property. It is the instance's value, not an inherited one — but code reading
// the own bag alone sees nothing there. That is how most of what the 3D viewer
// draws became unpickable and unfilterable: `ifcGlobalId` is exactly such a
// name, and so is `ifcClass`, which colours the graph.
//
// This reads own value first, then the overrides. It does not reach a value the
// Thing merely inherits without overriding — that resolves live from the type
// and needs the server-resolved view (GET /api/things/{id}/properties).

import type { VosThing } from '../types/vos';

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
