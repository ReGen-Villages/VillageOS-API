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
  DEFAULT_TERRAIN_EXAGGERATION,
  type BasemapSource,
  type DeclaredBasemapSource,
  type RaisedGround,
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
    const attribution = statedText(source.attribution);
    const styleUrl = statedText(source.styleUrl);
    const tileUrl = statedText(source.tileUrl);
    if (!attribution) continue;
    if (styleUrl && tileUrl) continue;
    const ground = raisedGroundFrom(source);
    if (styleUrl) {
      found.push({ ...ground, id: source.id, name: source.name, attribution, kind: 'style', styleUrl });
    } else if (tileUrl) {
      found.push({
        ...ground,
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

/**
 * What the source says about drawing land rather than a diagram.
 *
 * A pyramid stated without its encoding raises nothing: the schemes in common use pack a height into
 * the same three channels differently, so a map guessing between them would raise hills out of flat
 * ground and say nothing. The exaggeration falls back because it is a presentation choice with a
 * sensible default, where the other two are facts about the provider that only the model can know.
 */
function raisedGroundFrom(source: DeclaredBasemapSource): RaisedGround {
  const tileUrl = statedText(source.terrainTileUrl);
  const encoding = statedText(source.terrainEncoding);
  const buildingSourceLayer = statedText(source.buildingSourceLayer);

  return {
    ...(tileUrl && encoding
      ? {
          terrain: {
            tileUrl,
            encoding,
            exaggeration:
              typeof source.terrainExaggeration === 'number' && source.terrainExaggeration > 0
                ? source.terrainExaggeration
                : DEFAULT_TERRAIN_EXAGGERATION,
          },
        }
      : {}),
    ...(buildingSourceLayer ? { buildingSourceLayer } : {}),
  };
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

/** A value the model actually states, or nothing. A blank address and an absent one mean the same thing
 *  to a map, and the two arrive by different routes: one from a property left empty, one from a field the
 *  service answered as null. */
export function statedText(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}
