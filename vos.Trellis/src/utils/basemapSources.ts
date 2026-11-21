/** The signed-in page and the public form both ask, so the rule lives here and they cannot answer
 *  differently; the form is told its sources by the intake service, so its bundle cannot reach the broker. */
import type { StyleSpecification } from 'maplibre-gl';
import {
  DEFAULT_RASTER_MAXIMUM_ZOOM,
  DEFAULT_TERRAIN_EXAGGERATION,
  TERRAIN_ENCODINGS,
  type BasemapSource,
  type DeclaredBasemapSource,
  type RaisedGround,
} from '../types/basemap';

/** A source with no credit is dropped, because its licence requires the page to show the credit, and so is
 *  one stating both a style and a tile address, because nothing here may decide which the model meant.
 *  Ordered by name, so a model always opens on the same layer. */
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
 * A tile pyramid with no scheme a map can read raises nothing, because the schemes pack a height into the
 * same channels differently: guessing would raise hills out of flat ground, and a map given an unknown
 * scheme adds no source and then throws, which takes the page down. The exaggeration has a default because
 * it is a presentation choice; the other two are facts about the provider that only the model knows.
 */
function raisedGroundFrom(source: DeclaredBasemapSource): RaisedGround {
  const tileUrl = statedText(source.terrainTileUrl);
  const stated = statedText(source.terrainEncoding);
  const encoding = TERRAIN_ENCODINGS.find((readable) => readable === stated);
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

/** A blank address and an absent one mean the same to a map, and they arrive by different routes: a property
 *  left empty, or a field the service answered as null. */
export function statedText(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}
