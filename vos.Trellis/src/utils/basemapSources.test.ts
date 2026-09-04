import { describe, it, expect } from 'vitest';
import { basemapSourcesFrom, styleForSource } from './basemapSources';
import { DEFAULT_RASTER_MAXIMUM_ZOOM, DEFAULT_TERRAIN_EXAGGERATION } from '../types/basemap';

// What the intake service hands a public form: sources as the model states them, judged here rather than
// there, so the form and the signed-in page cannot come to different answers about the same model.
describe('basemapSourcesFrom', () => {
  it('reads a vector style source', () => {
    expect(
      basemapSourcesFrom([
        { id: 'src-1', name: 'Streets', attribution: 'Example', styleUrl: 'https://example.test/s.json' },
      ]),
    ).toEqual([
      {
        id: 'src-1',
        name: 'Streets',
        attribution: 'Example',
        kind: 'style',
        styleUrl: 'https://example.test/s.json',
      },
    ]);
  });

  it('reads a raster source and the depth it claims', () => {
    expect(
      basemapSourcesFrom([
        {
          id: 'src-1',
          name: 'Aerial',
          attribution: 'Example',
          tileUrl: 'https://example.test/{z}/{x}/{y}.png',
          maximumZoom: 17,
        },
      ])[0],
    ).toMatchObject({ kind: 'raster', maximumZoom: 17 });
  });

  it('falls back to a default depth when the model states none', () => {
    expect(
      basemapSourcesFrom([
        { id: 'src-1', name: 'Aerial', attribution: 'Example', tileUrl: 'https://example.test/{z}/{x}/{y}.png' },
      ])[0],
    ).toMatchObject({ maximumZoom: DEFAULT_RASTER_MAXIMUM_ZOOM });
  });

  it('refuses a source with no attribution, because displaying one bare breaks its licence', () => {
    expect(
      basemapSourcesFrom([{ id: 'src-1', name: 'Streets', styleUrl: 'https://example.test/s.json' }]),
    ).toEqual([]);
  });

  it('refuses a source that gives no address', () => {
    expect(basemapSourcesFrom([{ id: 'src-1', name: 'Streets', attribution: 'Example' }])).toEqual([]);
  });

  it('refuses a source giving both a style and a tile address, rather than guessing which was meant', () => {
    expect(
      basemapSourcesFrom([
        {
          id: 'src-1',
          name: 'Streets',
          attribution: 'Example',
          styleUrl: 'https://example.test/s.json',
          tileUrl: 'https://example.test/{z}/{x}/{y}.png',
        },
      ]),
    ).toEqual([]);
  });

  // The service answers an absent value as null, and a page that treated null as an address would draw a
  // map from the word.
  it('reads an absent value as absent whether it arrives as null or is left out', () => {
    expect(
      basemapSourcesFrom([
        {
          id: 'src-1',
          name: 'Streets',
          attribution: 'Example',
          styleUrl: 'https://example.test/s.json',
          tileUrl: null,
          maximumZoom: null,
        },
      ])[0],
    ).toMatchObject({ kind: 'style' });
  });

  it('offers sources in name order, so the same model always opens on the same layer', () => {
    const ordered = basemapSourcesFrom([
      { id: 'src-2', name: 'Streets', attribution: 'Example', styleUrl: 'https://example.test/s.json' },
      { id: 'src-1', name: 'Aerial', attribution: 'Example', tileUrl: 'https://example.test/{z}/{x}/{y}.png' },
    ]);
    expect(ordered.map((source) => source.name)).toEqual(['Aerial', 'Streets']);
  });
});

describe('styleForSource', () => {
  it('hands a vector style straight to the map', () => {
    expect(
      styleForSource({
        id: 'src-1',
        name: 'Streets',
        attribution: 'Example',
        kind: 'style',
        styleUrl: 'https://tiles.example.org/styles/plain',
      }),
    ).toBe('https://tiles.example.org/styles/plain');
  });

  it('wraps a raster pyramid in a style that carries its attribution and its depth', () => {
    const style = styleForSource({
      id: 'src-1',
      name: 'Aerial',
      attribution: 'Example national mapping agency',
      kind: 'raster',
      tileUrl: 'https://tiles.example.org/{z}/{x}/{y}.png',
      maximumZoom: 17,
    });
    expect(style).toEqual({
      version: 8,
      sources: {
        basemap: {
          type: 'raster',
          tiles: ['https://tiles.example.org/{z}/{x}/{y}.png'],
          tileSize: 256,
          maxzoom: 17,
          attribution: 'Example national mapping agency',
        },
      },
      layers: [{ id: 'basemap', type: 'raster', source: 'basemap' }],
    });
  });
});

// Feature #6912 — what the model says about drawing land rather than a diagram. Every part is
// optional, and a part stated without what it needs is dropped rather than half-applied: a height
// cannot be read off a tile pyramid without knowing how the pyramid packs one.
describe('what a source says about raising the ground', () => {
  const declared = {
    id: 'src-1',
    name: 'Streets',
    attribution: 'Example credit',
    styleUrl: 'https://tiles.example.org/styles/plain',
  };

  it('carries the elevation pyramid, its encoding and its exaggeration through', () => {
    const [source] = basemapSourcesFrom([
      {
        ...declared,
        terrainTileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
        terrainEncoding: 'terrarium',
        terrainExaggeration: 2,
        buildingSourceLayer: 'building',
      },
    ]);

    expect(source.terrain).toEqual({
      tileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
      encoding: 'terrarium',
      exaggeration: 2,
    });
    expect(source.buildingSourceLayer).toBe('building');
  });

  it('raises what the model states at the shipped exaggeration when it states none', () => {
    const [source] = basemapSourcesFrom([
      {
        ...declared,
        terrainTileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
        terrainEncoding: 'terrarium',
      },
    ]);

    expect(source.terrain?.exaggeration).toBe(DEFAULT_TERRAIN_EXAGGERATION);
  });

  it('raises nothing from a pyramid whose encoding the model never says', () => {
    const [source] = basemapSourcesFrom([
      { ...declared, terrainTileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png' },
    ]);

    expect(source.terrain).toBeUndefined();
  });

  // A word is not an encoding. The map refuses a scheme it cannot read by adding no source at all, and
  // then throws on being told to drape the ground over one — out of an effect, which takes the page
  // down. Refusing the word here is the same refusal as for a missing one, one step earlier.
  it('raises nothing from a pyramid under a scheme no map can read', () => {
    const [source] = basemapSourcesFrom([
      {
        ...declared,
        terrainTileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
        terrainEncoding: 'mapzen',
      },
    ]);

    expect(source.terrain).toBeUndefined();
  });

  it('leaves a source that says nothing about the ground flat, as every source is today', () => {
    const [source] = basemapSourcesFrom([declared]);

    expect(source.terrain).toBeUndefined();
    expect(source.buildingSourceLayer).toBeUndefined();
  });
});
