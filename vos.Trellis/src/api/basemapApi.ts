/** Reads basemap sources from the model the page already holds. The provider, and the credit its licence
 *  requires the page to show, are the model's answers, so no address or credit is written here. */
import type { ModelIndex } from '@tendril-ui/tendril';
import { thingsOfArchetypeBoundBy } from '@tendril-ui/tendril';
import { LAND_INTAKE_ROLE } from './landIntakeRoles';
import { effectiveProperties } from '@tendril-ui/tendril';
import { basemapSourcesFrom, statedText } from '../utils/basemapSources';
import {
  BASEMAP_ATTRIBUTION_PROPERTY,
  BASEMAP_BUILDING_LAYER_PROPERTY,
  BASEMAP_MAXIMUM_ZOOM_PROPERTY,
  BASEMAP_ORDER_PROPERTY,
  BASEMAP_STYLE_URL_PROPERTY,
  BASEMAP_TERRAIN_ENCODING_PROPERTY,
  BASEMAP_TERRAIN_EXAGGERATION_PROPERTY,
  BASEMAP_TERRAIN_URL_PROPERTY,
  BASEMAP_TILE_URL_PROPERTY,
  type BasemapSource,
} from '../types/basemap';

export function discoverBasemapSources(index: ModelIndex): BasemapSource[] {
  return basemapSourcesFrom(
    thingsOfArchetypeBoundBy(LAND_INTAKE_ROLE.BasemapSource, index).map((thing) => {
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
        order: statedNumber(properties[BASEMAP_ORDER_PROPERTY]),
      };
    }),
  );
}

function statedNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null;
}
