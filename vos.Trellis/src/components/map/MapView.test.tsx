import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import type { BasemapSource } from '../../types/basemap';

interface RecordedMap {
  styleLoaded: boolean;
  isStyleLoaded: () => boolean;
  finishLoadingTheStyle: () => void;
  options: Record<string, unknown>;
  handlers: Map<string, (event?: unknown) => void>;
  sources: Map<string, { setData: ReturnType<typeof vi.fn>; data: unknown }>;
  layers: string[];
  flyTo: ReturnType<typeof vi.fn>;
  remove: ReturnType<typeof vi.fn>;
  setStyle: ReturnType<typeof vi.fn>;
}

interface RecordedMarker {
  options: Record<string, unknown>;
  position: [number, number] | null;
  handlers: Map<string, () => void>;
  remove: ReturnType<typeof vi.fn>;
}

const mocks = vi.hoisted(() => {
  const mapInstances: unknown[] = [];
  const markerInstances: unknown[] = [];
  const markerPositions: [number, number][] = [];
  class MockMap {
    options: Record<string, unknown>;
    handlers = new Map<string, (event?: unknown) => void>();
    sources = new Map<string, { setData: (data: unknown) => void; data: unknown }>();
    layers: string[] = [];
    flyTo = vi.fn();
    remove = vi.fn();
    setStyle = vi.fn();
    /** A map begins with a style still loading, the way a real one does. */
    styleLoaded = false;
    constructor(options: Record<string, unknown>) {
      this.options = options;
      mapInstances.push(this);
    }
    isStyleLoaded() {
      return this.styleLoaded;
    }
    // maplibre refuses to *change* a style that has not finished loading, and refuses by throwing.
    // Reading one is allowed, which is why `getSource` below does not ask. A mock that answered the
    // mutating calls regardless would let a caller that asks too early pass.
    private insistStyleIsLoaded() {
      if (!this.styleLoaded) throw new Error('Style is not done loading.');
    }
    on(event: string, handler: (event?: unknown) => void) {
      this.handlers.set(event, handler);
    }
    off(event: string, handler: (event?: unknown) => void) {
      if (this.handlers.get(event) === handler) this.handlers.delete(event);
    }
    addSource(id: string, source: { data: unknown }) {
      this.insistStyleIsLoaded();
      const held = {
        data: source.data,
        setData: vi.fn((data: unknown) => {
          held.data = data;
        }),
      };
      this.sources.set(id, held);
    }
    getSource(id: string) {
      return this.sources.get(id);
    }
    removeSource(id: string) {
      this.insistStyleIsLoaded();
      this.sources.delete(id);
    }
    addLayer(layer: { id: string }) {
      this.insistStyleIsLoaded();
      this.layers.push(layer.id);
    }
    removeLayer(id: string) {
      this.insistStyleIsLoaded();
      this.layers = this.layers.filter((held) => held !== id);
    }
    /** What maplibre does once the style is in: the map answers, and says so. */
    finishLoadingTheStyle() {
      this.styleLoaded = true;
      this.handlers.get('styledata')?.();
    }
  }
  class MockMarker {
    options: Record<string, unknown>;
    position: [number, number] | null = null;
    handlers = new Map<string, () => void>();
    remove = vi.fn();
    constructor(options: Record<string, unknown> = {}) {
      this.options = options;
      markerInstances.push(this);
    }
    setLngLat(position: [number, number]) {
      this.position = position;
      markerPositions.push(position);
      return this;
    }
    addTo() {
      return this;
    }
    getLngLat() {
      return { lng: this.position![0], lat: this.position![1] };
    }
    on(event: string, handler: () => void) {
      this.handlers.set(event, handler);
    }
  }
  const config = { WORKER_URL: '' };
  return { mapInstances, markerInstances, markerPositions, MockMap, MockMarker, config };
});

vi.mock('maplibre-gl', () => ({
  Map: mocks.MockMap,
  Marker: mocks.MockMarker,
  config: mocks.config,
}));
vi.mock('maplibre-gl/dist/maplibre-gl.css', () => ({}));
vi.mock('maplibre-gl/dist/maplibre-gl-worker.mjs?url', () => ({
  default: 'https://example.test/assets/maplibre-worker.mjs',
}));

import { MapView } from './MapView';
import { useMapStore } from '../../stores/mapStore';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

const maps = mocks.mapInstances as RecordedMap[];
const markers = mocks.markerInstances as RecordedMarker[];
const markerPositions = mocks.markerPositions;

const draggable = () => markers.filter((marker) => marker.options.draggable === true);

function source(name: string, styleUrl: string): BasemapSource {
  return { id: `id-${name}`, name, attribution: `${name} credit`, kind: 'style', styleUrl };
}

const STREETS = source('Streets', 'https://tiles.example.org/streets');
const AERIAL = source('Aerial', 'https://tiles.example.org/aerial');

const POSITION = { latitude: 41.38, longitude: -70.64 };

beforeEach(() => {
  maps.length = 0;
  markers.length = 0;
  markerPositions.length = 0;
  useMapStore.setState({ selectedSourceName: null, tilesUnreachable: false });
});

describe('MapView', () => {

  // Lose this and the map still mounts, still draws its controls and still reports no error — it
  // simply never parses a tile. See the reason in MapView.
  it('points maplibre at the worker the bundler emitted', () => {
    expect(mocks.config.WORKER_URL).toBe('https://example.test/assets/maplibre-worker.mjs');
  });

  it('says so and keeps the coordinates readable when the model declares no source', () => {
    render(<MapView {...POSITION} sources={[]} />);
    expect(screen.getByText('This model defines no basemap source.')).toBeTruthy();
    expect(screen.getByText('41.38000, -70.64000')).toBeTruthy();
    expect(maps).toHaveLength(0);
  });

  it('opens the map on the given point with the style the model supplied', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    // Let the style land on a map given no boundary: it draws none, and takes none off.
    act(() => maps[0].finishLoadingTheStyle());
    expect(maps[0].sources.has('boundary')).toBe(false);
    expect(maps[0].options.center).toEqual([-70.64, 41.38]);
    expect(maps[0].setStyle).toHaveBeenCalledWith('https://tiles.example.org/streets');
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
    expect(maps[0].setStyle).toHaveBeenLastCalledWith('https://tiles.example.org/streets');
  });

  it('swaps the style rather than rebuilding, so a layer switch keeps where the reader had panned to', () => {
    render(<MapView {...POSITION} sources={[AERIAL, STREETS]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Streets' }));
    expect(maps).toHaveLength(1);
    expect(maps[0].remove).not.toHaveBeenCalled();
  });

  it('returns the view to the marker when asked to recentre', () => {
    render(<MapView {...POSITION} sources={[STREETS]} initialZoom={12} />);
    fireEvent.click(screen.getByRole('button', { name: 'Recentre' }));
    expect(maps[0].flyTo).toHaveBeenCalledWith({ center: [-70.64, 41.38], zoom: 12 });
  });

  it('follows a moved position without rebuilding the map', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} />);
    rerender(<MapView latitude={40.1} longitude={-70.2} sources={[STREETS]} />);
    expect(maps).toHaveLength(1);
    expect(markerPositions).toEqual([
      [-70.64, 41.38],
      [-70.2, 40.1],
    ]);
    expect(maps[0].flyTo).toHaveBeenCalledWith({ center: [-70.2, 40.1], zoom: 15 });
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

describe('the boundary on the map', () => {
  const CORNERS: readonly BoundaryPoint[] = [
    { latitude: 41.38, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.638 },
  ];

  function ring(): [number, number][] {
    const feature = maps[0].sources.get('boundary')!.data as {
      geometry: { coordinates: [number, number][][] };
    };
    return feature.geometry.coordinates[0];
  }

  // Leaving the Parcel step and coming back builds a second map with the boundary already in hand, so
  // it is drawn the moment the map exists — against a style that has not finished loading. maplibre
  // refuses that by throwing, and a throw in this effect unmounts the form, losing the page a
  // submitter was filling in.
  it('waits for the style rather than throwing when it opens with a boundary already drawn', () => {
    expect(() =>
      render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />),
    ).not.toThrow();
    expect(maps[0].sources.has('boundary')).toBe(false);

    act(() => maps[0].finishLoadingTheStyle());

    expect(maps[0].sources.has('boundary')).toBe(true);
  });

  it('draws the boundary as a closed ring over the basemap', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    act(() => maps[0].finishLoadingTheStyle());

    expect(ring()).toHaveLength(CORNERS.length + 1);
    expect(ring()[0]).toEqual([-70.64, 41.38]);
    expect(ring()[CORNERS.length]).toEqual(ring()[0]);
  });

  it('draws it again after a style swap wiped what the style held', () => {
    render(<MapView {...POSITION} sources={[AERIAL, STREETS]} boundary={CORNERS} />);
    act(() => maps[0].finishLoadingTheStyle());
    fireEvent.click(screen.getByRole('button', { name: 'Streets' }));
    maps[0].sources.clear();
    maps[0].layers.length = 0;

    act(() => maps[0].handlers.get('styledata')!());

    expect(maps[0].sources.has('boundary')).toBe(true);
    expect(maps[0].layers).toContain('boundary-fill');
  });

  it('adds a corner where the map is clicked, when a change handler makes it editable', () => {
    const onBoundaryChange = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={[]} onBoundaryChange={onBoundaryChange} />);

    act(() => maps[0].handlers.get('click')!({ lngLat: { lat: 41.384, lng: -70.636 } }));

    expect(onBoundaryChange).toHaveBeenCalledWith([{ latitude: 41.384, longitude: -70.636 }]);
  });

  it('reports the whole boundary when one corner is dragged somewhere else', () => {
    const onBoundaryChange = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} onBoundaryChange={onBoundaryChange} />);
    const corner = draggable()[1];

    corner.position = [-70.637, 41.383];
    act(() => corner.handlers.get('dragend')!());

    expect(onBoundaryChange).toHaveBeenCalledWith([
      CORNERS[0],
      { latitude: 41.383, longitude: -70.637 },
      CORNERS[2],
    ]);
  });

  it('redraws a moved boundary in place rather than adding a second one', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    act(() => maps[0].finishLoadingTheStyle());
    const moved = [CORNERS[0], { latitude: 41.383, longitude: -70.637 }, CORNERS[2]];

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={moved} />);

    expect(maps[0].sources.get('boundary')!.setData).toHaveBeenCalled();
    expect(ring()[1]).toEqual([-70.637, 41.383]);
    expect(maps[0].layers).toEqual(['boundary-fill', 'boundary-line']);
  });

  it('offers no drawing at all without a change handler', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);

    expect(maps[0].handlers.has('click')).toBe(false);
    expect(draggable()).toHaveLength(0);
  });

  it('takes a cleared boundary off the map, corners and outline both', () => {
    const onBoundaryChange = vi.fn();
    const { rerender } = render(
      <MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} onBoundaryChange={onBoundaryChange} />,
    );
    act(() => maps[0].finishLoadingTheStyle());
    const corners = draggable();
    expect(corners).toHaveLength(CORNERS.length);
    // Drawn first, or "it was taken off" passes on a boundary that was never put on.
    expect(maps[0].sources.has('boundary')).toBe(true);

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={[]} onBoundaryChange={onBoundaryChange} />);

    corners.forEach((corner) => expect(corner.remove).toHaveBeenCalled());
    expect(maps[0].sources.has('boundary')).toBe(false);
    expect(maps[0].layers).toEqual([]);
  });
});
