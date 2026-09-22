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
import { basemapSourcesFrom, statedText } from '../utils/basemapSources';
import {
  BASEMAP_ATTRIBUTION_PROPERTY,
  BASEMAP_BUILDING_LAYER_PROPERTY,
  BASEMAP_MAXIMUM_ZOOM_PROPERTY,
  BASEMAP_SOURCE_ARCHETYPE,
  BASEMAP_STYLE_URL_PROPERTY,
  BASEMAP_TERRAIN_ENCODING_PROPERTY,
  BASEMAP_TERRAIN_EXAGGERATION_PROPERTY,
  BASEMAP_TERRAIN_URL_PROPERTY,
  BASEMAP_TILE_URL_PROPERTY,
  type BasemapSource,
} from '../types/basemap';

export function discoverBasemapSources(index: ModelIndex): BasemapSource[] {
  return basemapSourcesFrom(
    thingsOfArchetype(BASEMAP_SOURCE_ARCHETYPE, index).map((thing) => {
      const properties = effectiveProperties(thing, index);
      return {
        id: thing.Id,
        name: thing.Name,
        attribution: statedText(properties[BASEMAP_ATTRIBUTION_PROPERTY]),
        styleUrl: statedText(properties[BASEMAP_STYLE_URL_PROPERTY]),
        tileUrl: statedText(properties[BASEMAP_TILE_URL_PROPERTY]),
        maximumZoom: statedNumber(properties[BASEMAP_MAXIMUM_ZOOM_PROPERTY]),
        terrainTileUrl: statedText(properties[BASEMAP_TERRAIN_URL_PROPERTY]),
        terrainEncoding: statedText(properties[BASEMAP_TERRAIN_ENCODING_PROPERTY]),
        terrainExaggeration: statedNumber(properties[BASEMAP_TERRAIN_EXAGGERATION_PROPERTY]),
        buildingSourceLayer: statedText(properties[BASEMAP_BUILDING_LAYER_PROPERTY]),
      };
    }),
  );
}

function statedNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null;
}
