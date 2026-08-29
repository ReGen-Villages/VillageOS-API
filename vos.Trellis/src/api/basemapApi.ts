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
  type DeclaredBasemapSource,
} from '../types/basemap';

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

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

/**
 * What makes a stated source drawable, in the one place both the page that reads the model and the page
 * that is told about it by the intake service ask.
 *
 * A source with no credit is dropped rather than drawn, because the credit is what its licence obliges
 * the page to display. A source stating both a style and a tile address is dropped too: the two load
 * differently and nothing here may decide which the model meant.
 */
export function basemapSourcesFrom(declared: readonly DeclaredBasemapSource[]): BasemapSource[] {
  const found: BasemapSource[] = [];
  for (const source of declared) {
    const attribution = text(source.attribution);
    const styleUrl = text(source.styleUrl);
    const tileUrl = text(source.tileUrl);
    if (!attribution) continue;
    if (styleUrl && tileUrl) continue;
    if (styleUrl) {
      found.push({ id: source.id, name: source.name, attribution, kind: 'style', styleUrl });
    } else if (tileUrl) {
      found.push({
        id: source.id,
        name: source.name,
        attribution,
        kind: 'raster',
        tileUrl,
        maximumZoom:
          typeof source.maximumZoom === 'number' ? source.maximumZoom : DEFAULT_RASTER_MAXIMUM_ZOOM,
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
