import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import type { BasemapSource } from '../../types/basemap';

interface RecordedMap {
  options: Record<string, unknown>;
  handlers: Map<string, () => void>;
  flyTo: ReturnType<typeof vi.fn>;
  remove: ReturnType<typeof vi.fn>;
}

const mocks = vi.hoisted(() => {
  const mapInstances: unknown[] = [];
  const markerPositions: [number, number][] = [];
  class MockMap {
    options: Record<string, unknown>;
    handlers = new Map<string, () => void>();
    flyTo = vi.fn();
    remove = vi.fn();
    constructor(options: Record<string, unknown>) {
      this.options = options;
      mapInstances.push(this);
    }
    on(event: string, handler: () => void) {
      this.handlers.set(event, handler);
    }
  }
  class MockMarker {
    setLngLat(position: [number, number]) {
      markerPositions.push(position);
      return this;
    }
    addTo() {
      return this;
    }
  }
  return { mapInstances, markerPositions, MockMap, MockMarker };
});

vi.mock('maplibre-gl', () => ({ Map: mocks.MockMap, Marker: mocks.MockMarker }));
vi.mock('maplibre-gl/dist/maplibre-gl.css', () => ({}));

import { MapView } from './MapView';
import { useMapStore } from '../../stores/mapStore';

const maps = mocks.mapInstances as RecordedMap[];
const markerPositions = mocks.markerPositions;

function source(name: string, styleUrl: string): BasemapSource {
  return { id: `id-${name}`, name, attribution: `${name} credit`, kind: 'style', styleUrl };
}

const STREETS = source('Streets', 'https://tiles.example.org/streets');
const AERIAL = source('Aerial', 'https://tiles.example.org/aerial');

const POSITION = { latitude: 41.38, longitude: -70.64 };

describe('MapView', () => {
  beforeEach(() => {
    maps.length = 0;
    markerPositions.length = 0;
    useMapStore.setState({ selectedSourceName: null, tilesUnreachable: false });
  });

  it('says so and keeps the coordinates readable when the model declares no source', () => {
    render(<MapView {...POSITION} sources={[]} />);
    expect(screen.getByText('This model defines no basemap source.')).toBeTruthy();
    expect(screen.getByText('41.38000, -70.64000')).toBeTruthy();
    expect(maps).toHaveLength(0);
  });

  it('opens the map on the given point with the style the model supplied', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    expect(maps[0].options.style).toBe('https://tiles.example.org/streets');
    expect(maps[0].options.center).toEqual([-70.64, 41.38]);
    expect(markerPositions).toEqual([[-70.64, 41.38]]);
  });

  it('offers no layer switch when the model holds a single source', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    expect(screen.queryByRole('group')).toBeNull();
  });

  it('offers every source the model holds and switches to the one pressed', () => {
    render(<MapView {...POSITION} sources={[AERIAL, STREETS]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Streets' }));
    expect(useMapStore.getState().selectedSourceName).toBe('Streets');
    expect(maps.at(-1)!.options.style).toBe('https://tiles.example.org/streets');
  });

  it('returns the view to the marker when asked to recentre', () => {
    render(<MapView {...POSITION} sources={[STREETS]} initialZoom={12} />);
    fireEvent.click(screen.getByRole('button', { name: 'Recentre' }));
    expect(maps[0].flyTo).toHaveBeenCalledWith({ center: [-70.64, 41.38], zoom: 12 });
  });

  it('reports unreachable tiles without taking the coordinates away', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    act(() => maps[0].handlers.get('error')!());
    expect(screen.getByRole('status').textContent).toBe('Map tiles could not be loaded.');
    expect(screen.getByText('41.38000, -70.64000')).toBeTruthy();
  });

  it('tears the map down on unmount, so a page revisit does not leak the previous one', () => {
    const { unmount } = render(<MapView {...POSITION} sources={[STREETS]} />);
    unmount();
    expect(maps[0].remove).toHaveBeenCalledTimes(1);
  });
});
