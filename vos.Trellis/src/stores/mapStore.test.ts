import { describe, it, expect, beforeEach } from 'vitest';
import type { BasemapSource } from '../types/basemap';
import { useMapStore, resolveSelectedSource } from './mapStore';

function styleSource(name: string): BasemapSource {
  return {
    id: `id-${name}`,
    name,
    attribution: 'Example',
    kind: 'style',
    styleUrl: `https://tiles.example.org/${name}`,
  };
}

const AERIAL = styleSource('Aerial');
const STREETS = styleSource('Streets');

describe('resolveSelectedSource', () => {
  it('returns the source whose name was chosen', () => {
    expect(resolveSelectedSource([AERIAL, STREETS], 'Streets')).toBe(STREETS);
  });

  it('falls back to the first source when nothing has been chosen', () => {
    expect(resolveSelectedSource([AERIAL, STREETS], null)).toBe(AERIAL);
  });

  it('falls back to the first source when the chosen name is not in this model', () => {
    expect(resolveSelectedSource([AERIAL, STREETS], 'Nautical')).toBe(AERIAL);
  });

  it('returns nothing when the model holds no source at all', () => {
    expect(resolveSelectedSource([], 'Streets')).toBeNull();
  });
});

describe('useMapStore', () => {
  beforeEach(() => {
    useMapStore.setState({ selectedSourceName: null, tilesUnreachable: false });
  });

  it('remembers the chosen layer so leaving the page and coming back keeps it', () => {
    useMapStore.getState().selectSource('Streets');
    expect(useMapStore.getState().selectedSourceName).toBe('Streets');
  });

  it('clears an earlier tile failure when the reader picks a different layer', () => {
    useMapStore.setState({ tilesUnreachable: true });
    useMapStore.getState().selectSource('Streets');
    expect(useMapStore.getState().tilesUnreachable).toBe(false);
  });

  it('records that tiles could not be reached', () => {
    useMapStore.getState().reportTilesUnreachable();
    expect(useMapStore.getState().tilesUnreachable).toBe(true);
  });
});
