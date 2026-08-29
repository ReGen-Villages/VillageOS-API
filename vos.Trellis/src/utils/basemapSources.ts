/**
 * What makes a stated basemap source drawable, and how the map loads one.
 *
 * Two pages ask: the signed-in one, which reads the sources out of the model it already holds, and the
 * public form, which is told them by the intake service because it cannot read a model at all. The rule
 * lives here so the two cannot come to different answers, and so the form's bundle carries no way of
 * reaching the broker.
 */
import type { StyleSpecification } from 'maplibre-gl';
import {
  DEFAULT_RASTER_MAXIMUM_ZOOM,
  type BasemapSource,
  type DeclaredBasemapSource,
} from '../types/basemap';

/**
 * A source with no credit is dropped rather than drawn, because the credit is what its licence obliges
 * the page to display. A source stating both a style and a tile address is dropped too: the two load
 * differently and nothing here may decide which the model meant.
 *
 * Ordered by name, so the same model always opens on the same layer.
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

export function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}
