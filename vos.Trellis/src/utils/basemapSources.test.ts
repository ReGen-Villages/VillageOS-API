import { describe, it, expect } from 'vitest';
import { basemapSourcesFrom, styleForSource } from './basemapSources';
import { DEFAULT_RASTER_MAXIMUM_ZOOM } from '../types/basemap';

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
