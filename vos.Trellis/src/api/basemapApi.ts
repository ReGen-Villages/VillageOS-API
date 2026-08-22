/**
 * Basemap discovery.
 *
 * A model declares Things of archetype `BasemapSource` (see BASEMAP_SOURCE_ARCHETYPE).
 * This reads them out of the already-loaded model store and turns each into something
 * the map can draw. Which provider a deployment uses, and what its licence obliges the
 * page to display, are answers the model gives — no address or credit is written here.
 */
import type { ModelIndex } from './dashboardApi';
import type { StyleSpecification } from 'maplibre-gl';
import { thingsOfArchetype } from './dashboardApi';
import { effectiveProperties } from '../utils/propertyMapper';
import {
  BASEMAP_ATTRIBUTION_PROPERTY,
  BASEMAP_MAXIMUM_ZOOM_PROPERTY,
  BASEMAP_SOURCE_ARCHETYPE,
  BASEMAP_STYLE_URL_PROPERTY,
  BASEMAP_TILE_URL_PROPERTY,
  DEFAULT_RASTER_MAXIMUM_ZOOM,
  type BasemapSource,
} from '../types/basemap';

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

export function discoverBasemapSources(index: ModelIndex): BasemapSource[] {
  const found: BasemapSource[] = [];
  for (const thing of thingsOfArchetype(BASEMAP_SOURCE_ARCHETYPE, index)) {
    const properties = effectiveProperties(thing, index);
    const attribution = text(properties[BASEMAP_ATTRIBUTION_PROPERTY]);
    const styleUrl = text(properties[BASEMAP_STYLE_URL_PROPERTY]);
    const tileUrl = text(properties[BASEMAP_TILE_URL_PROPERTY]);
    if (!attribution) continue;
    if (styleUrl && tileUrl) continue;
    if (styleUrl) {
      found.push({ id: thing.Id, name: thing.Name, attribution, kind: 'style', styleUrl });
    } else if (tileUrl) {
      const stated = properties[BASEMAP_MAXIMUM_ZOOM_PROPERTY];
      found.push({
        id: thing.Id,
        name: thing.Name,
        attribution,
        kind: 'raster',
        tileUrl,
        maximumZoom: typeof stated === 'number' ? stated : DEFAULT_RASTER_MAXIMUM_ZOOM,
      });
    }
  }
  found.sort((a, b) => a.name.localeCompare(b.name));
  return found;
}

export function styleForSource(source: BasemapSource): StyleSpecification | string {
  if (source.kind === 'style') return source.styleUrl;
  return {
    version: 8,
    sources: {
      basemap: {
        type: 'raster',
        tiles: [source.tileUrl],
        tileSize: 256,
        maxzoom: source.maximumZoom,
        attribution: source.attribution,
      },
    },
    layers: [{ id: 'basemap', type: 'raster', source: 'basemap' }],
  };
}
