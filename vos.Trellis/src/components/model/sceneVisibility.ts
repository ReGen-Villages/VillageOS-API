// What the type filter says the 3D scene should show, and how the viewer turns
// that into a set of scene items to hide.
//
// A Fragments artifact holds every element the IFC had; the model holds Things
// only for the ones it was given. So a list of IFC identifiers built from Things
// covers a fraction of the scene, and cannot express "show nothing" at all —
// which is why unchecking every type once left the model on screen.

import type { VosThing, VosRelationship } from '../../types/vos';
import { applyTypeFilter } from '../../utils/typeFilter';

export type SceneVisibility =
  | { kind: 'everything' }
  | { kind: 'nothing' }
  | { kind: 'everythingExcept'; hiddenIfcGuids: readonly string[] };

/**
 * What the 3D scene should show once `hiddenTypeIds` is applied.
 *
 * When the filter leaves no Thing standing the answer is `nothing`, stated
 * outright: naming identifiers to hide would reach only the elements the model
 * knows, leaving the rest of the scene drawn. A model with no Things at all is
 * not "fully hidden" — there is nothing to have hidden, so the scene is left
 * alone rather than blanked.
 */
export function sceneVisibilityFor(
  things: VosThing[],
  relationships: VosRelationship[],
  hiddenTypeIds: ReadonlySet<string>,
): SceneVisibility {
  if (hiddenTypeIds.size === 0) return { kind: 'everything' };

  const remaining = applyTypeFilter(things, relationships, hiddenTypeIds).things;
  if (things.length > 0 && remaining.length === 0) return { kind: 'nothing' };

  const stillShowing = new Set(remaining.map((t) => t.Id));
  const hiddenIfcGuids: string[] = [];
  for (const thing of things) {
    if (stillShowing.has(thing.Id)) continue;
    const guid = thing.Properties?.ifcGlobalId;
    if (typeof guid === 'string' && guid.length > 0) hiddenIfcGuids.push(guid);
  }
  return { kind: 'everythingExcept', hiddenIfcGuids };
}

/** The part of the Fragments model this module needs, so it can be faked in tests. */
export interface SceneItemSource {
  getItemsByVisibility(visible: boolean): Promise<number[]>;
  getLocalIdsByGuids(guids: string[]): Promise<(number | null | undefined)[]>;
}

/**
 * The scene items to hide, given what the filter says and what the model can
 * resolve. Callers reset visibility first, so an empty result means "show all".
 *
 * For `nothing` this asks the scene what it is currently showing rather than
 * translating identifiers, because the scene holds elements the model has no
 * Thing for and those would otherwise stay drawn.
 */
export async function hiddenSceneItemsFor(
  visibility: SceneVisibility,
  source: SceneItemSource,
): Promise<number[]> {
  if (visibility.kind === 'everything') return [];
  if (visibility.kind === 'nothing') return source.getItemsByVisibility(true);
  if (visibility.hiddenIfcGuids.length === 0) return [];
  const resolved = await source.getLocalIdsByGuids([...visibility.hiddenIfcGuids]);
  return resolved.filter((id): id is number => typeof id === 'number');
}
