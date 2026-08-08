// Reading a Thing's IFC identifier, wherever it is stored.
//
// Bug #6191: an IFC instance and the type it `is`-relates to both carry an
// ifcGlobalId, so the instance's own value lands in InheritedOverrides — the
// per-instance override store — rather than in its own properties. It is the
// instance's real identifier, not an inherited one, but code that read only
// own properties saw nothing: on the example model that left roughly one Thing
// in eight mappable to the geometry the viewer draws, so most elements could
// not be picked and the type filter could not reach them.

import type { VosThing } from '../types/vos';

const IFC_GLOBAL_ID = 'ifcGlobalId';

/**
 * The Thing's IFC GlobalId — its own if it has one, otherwise the override it
 * stores for the name. Returns null when it has neither, which is ordinary:
 * materials, classifications and anything not rooted in the IFC have no
 * identifier at all.
 */
export function ifcGlobalIdOf(thing: VosThing): string | null {
  const own = thing.Properties?.[IFC_GLOBAL_ID];
  if (typeof own === 'string' && own.length > 0) return own;

  for (const set of Object.values(thing.InheritedOverrides ?? {})) {
    const overridden = set?.Properties?.[IFC_GLOBAL_ID];
    if (typeof overridden === 'string' && overridden.length > 0) return overridden;
  }
  return null;
}
