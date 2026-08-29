/**
 * Basemap discovery from a model the page already holds.
 *
 * A model declares Things of archetype `BasemapSource` (see BASEMAP_SOURCE_ARCHETYPE). This reads them
 * out of the already-loaded model store and hands them to the shared rule that says which can be drawn.
 * Which provider a deployment uses, and what its licence obliges the page to display, are answers the
 * model gives — no address or credit is written here.
 */
import type { ModelIndex } from './dashboardApi';
import { thingsOfArchetype } from './dashboardApi';
import { effectiveProperties } from '../utils/propertyMapper';
import { basemapSourcesFrom, text } from '../utils/basemapSources';
import {
  BASEMAP_ATTRIBUTION_PROPERTY,
  BASEMAP_MAXIMUM_ZOOM_PROPERTY,
  BASEMAP_SOURCE_ARCHETYPE,
  BASEMAP_STYLE_URL_PROPERTY,
  BASEMAP_TILE_URL_PROPERTY,
  type BasemapSource,
} from '../types/basemap';

export function discoverBasemapSources(index: ModelIndex): BasemapSource[] {
  return basemapSourcesFrom(
    thingsOfArchetype(BASEMAP_SOURCE_ARCHETYPE, index).map((thing) => {
      const properties = effectiveProperties(thing, index);
      const maximumZoom = properties[BASEMAP_MAXIMUM_ZOOM_PROPERTY];
      return {
        id: thing.Id,
        name: thing.Name,
        attribution: text(properties[BASEMAP_ATTRIBUTION_PROPERTY]),
        styleUrl: text(properties[BASEMAP_STYLE_URL_PROPERTY]),
        tileUrl: text(properties[BASEMAP_TILE_URL_PROPERTY]),
        maximumZoom: typeof maximumZoom === 'number' ? maximumZoom : null,
      };
    }),
  );
}
