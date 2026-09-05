import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import type { BasemapSource } from '../../types/basemap';

interface RecordedMap {
  finishLoadingTheStyle: () => void;
  settle: () => void;
  fire: (event: string, payload?: unknown) => void;
  options: Record<string, unknown>;
  handlers: Map<string, Set<(event?: unknown) => void>>;
  sources: Map<string, { setData: ReturnType<typeof vi.fn>; data: unknown }>;
  layers: string[];
  flyTo: ReturnType<typeof vi.fn>;
  easeTo: ReturnType<typeof vi.fn>;
  remove: ReturnType<typeof vi.fn>;
  setStyle: ReturnType<typeof vi.fn>;
  setTerrain: ReturnType<typeof vi.fn>;
  setSky: ReturnType<typeof vi.fn>;
  setProjection: ReturnType<typeof vi.fn>;
  resetNorth: ReturnType<typeof vi.fn>;
  styleLoaded: boolean;
  terrain: unknown;
  sky: unknown;
  projection: { type: string } | undefined;
  maxPitch: number;
  bearing: number;
  addedLayers: { id: string; [key: string]: unknown }[];
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
  /** The encodings the style specification names for an elevation pyramid. Anything else fails its
   *  validation, which is what makes a source declaring one silently absent. */
  const READABLE_ENCODINGS = new Set(['terrarium', 'mapbox', 'custom']);
  const LIBRARY_PITCH_CEILING = 60;
  class MockMap {
    options: Record<string, unknown>;
    /** Every handler on an event, not the last one: the map carries two on `styledata`, and a mock
     *  keeping one of them would let a change that evicted the other pass. */
    handlers = new Map<string, Set<(event?: unknown) => void>>();
    sources = new Map<string, { setData: (data: unknown) => void; data: unknown }>();
    layers: string[] = [];
    flyTo = vi.fn();
    easeTo = vi.fn();
    remove = vi.fn();
    setStyle = vi.fn();
    /** What the map is draped over, which is nothing until something raises it. */
    terrain: unknown = null;
    /** What stands over the land and what it is drawn on: nothing and a flat plane, which is what a
     *  style declaring neither leaves. */
    sky: unknown = undefined;
    projection: { type: string } | undefined = undefined;
    /** The library's own ceiling on how far over the camera may lean. */
    maxPitch = LIBRARY_PITCH_CEILING;
    bearing = 0;
    /** Every layer as it was added, for a test asking how one was built rather than whether it is
     *  there. */
    addedLayers: { id: string; [key: string]: unknown }[] = [];
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
      const held = this.handlers.get(event) ?? new Set<(event?: unknown) => void>();
      held.add(handler);
      this.handlers.set(event, held);
    }
    off(event: string, handler: (event?: unknown) => void) {
      const held = this.handlers.get(event);
      held?.delete(handler);
      if (held?.size === 0) this.handlers.delete(event);
    }
    // maplibre validates a source against the style specification and, where it fails, fires an error
    // and adds nothing rather than throwing. A caller that then names it is the one that throws.
    addSource(id: string, source: { data?: unknown; [key: string]: unknown }) {
      this.insistStyleIsLoaded();
      if (source.type === 'raster-dem' && !READABLE_ENCODINGS.has(source.encoding as string)) return;
      const held = {
        ...source,
        data: source.data,
        setData: vi.fn((data: unknown) => {
          held.data = data;
        }),
      };
      this.sources.set(id, held);
    }
    setTerrain = vi.fn((terrain: { source: string } | null) => {
      this.insistStyleIsLoaded();
      if (terrain && !this.sources.has(terrain.source)) {
        throw new Error(`cannot load terrain, because there exists no source with ID: ${terrain.source}`);
      }
      this.terrain = terrain;
    });
    getTerrain() {
      return this.terrain;
    }
    setSky = vi.fn((sky: unknown) => {
      this.insistStyleIsLoaded();
      this.sky = sky;
    });
    getSky() {
      return this.sky;
    }
    setProjection = vi.fn((projection: { type: string }) => {
      this.insistStyleIsLoaded();
      this.projection = projection;
    });
    getProjection() {
      return this.projection;
    }
    setMaxPitch(maxPitch: number | null) {
      this.maxPitch = maxPitch ?? LIBRARY_PITCH_CEILING;
    }
    getBearing() {
      return this.bearing;
    }
    resetNorth = vi.fn(() => {
      this.bearing = 0;
      this.fire('rotate');
    });
    getLayer(id: string) {
      return this.layers.includes(id) ? { id } : undefined;
    }
    /** A vector style names its own sources, which is how a caller finds the one holding the
     *  footprints without knowing anything about the provider. */
    getStyle() {
      return { sources: { openmaptiles: { type: 'vector' } }, layers: [] };
    }
    getSource(id: string) {
      return this.sources.get(id);
    }
    removeSource(id: string) {
      this.insistStyleIsLoaded();
      this.sources.delete(id);
    }
    addLayer(layer: { id: string; [key: string]: unknown }) {
      this.insistStyleIsLoaded();
      this.layers.push(layer.id);
      this.addedLayers.push(layer);
    }
    removeLayer(id: string) {
      this.insistStyleIsLoaded();
      this.layers = this.layers.filter((held) => held !== id);
    }
    /** What maplibre does once the style is in: the map answers, and says so. */
    finishLoadingTheStyle() {
      this.styleLoaded = true;
      this.fire('styledata');
    }
    /** The map saying it has drawn everything it holds. It says so again after every repaint, which
     *  is what makes work done here cost on every pan and zoom rather than once. */
    settle() {
      this.fire('idle');
    }
    fire(event: string, payload?: unknown) {
      for (const handler of [...(this.handlers.get(event) ?? [])]) handler(payload);
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
vi.mock('maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url', () => ({
  default: 'https://example.test/assets/maplibre-worker.mjs',
}));

import { MapView } from './MapView';
import { useMapStore } from '../../stores/mapStore';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

const maps = mocks.mapInstances as RecordedMap[];
const markers = mocks.markerInstances as RecordedMarker[];
const markerPositions = mocks.markerPositions;

const draggable = () => markers.filter((marker) => marker.options.draggable === true);

/** A map draws nothing until its style is in, so a test about what is drawn has to let it arrive. */
const theStyleArrives = () => act(() => maps[0].finishLoadingTheStyle());

function source(name: string, styleUrl: string): BasemapSource {
  return { id: `id-${name}`, name, attribution: `${name} credit`, kind: 'style', styleUrl };
}

const STREETS = source('Streets', 'https://tiles.example.org/streets');
const AERIAL = source('Aerial', 'https://tiles.example.org/aerial');
/** A source declaring everything the model may say about the ground. */
const RAISED: BasemapSource = {
  ...source('Streets', 'https://tiles.example.org/streets'),
  terrain: {
    tileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
    encoding: 'terrarium',
    exaggeration: 1.4,
  },
  buildingSourceLayer: 'building',
};

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
    theStyleArrives();
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
    act(() => maps[0].fire('error'));
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

    theStyleArrives();

    expect(maps[0].sources.has('boundary')).toBe(true);
  });

  // What a live run showed: every `styledata` a real map fires arrives while its sources are still
  // loading, and nothing fires another once they are in. A boundary the map opened with waited for a
  // moment that never came, and the corner handles stood around nothing. `idle` is the map saying it
  // has drawn everything it has, and is when the boundary goes in — as the ground already does.
  it('draws a boundary it opened with on the first idle, where every styledata came too early', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    act(() => maps[0].fire('styledata'));
    expect(maps[0].sources.has('boundary')).toBe(false);

    act(() => {
      maps[0].styleLoaded = true;
      maps[0].settle();
    });

    expect(maps[0].sources.has('boundary')).toBe(true);
  });

  // Rewriting the boundary's data repaints the map, which settles into another idle. Drawn on each of
  // those, the map never stops.
  it('leaves a drawn boundary alone, however often the map settles', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();
    const drawn = maps[0].sources.get('boundary')!;

    act(() => {
      maps[0].settle();
      maps[0].settle();
    });

    expect(drawn.setData).not.toHaveBeenCalled();
    expect(maps[0].layers).toEqual(['boundary-fill', 'boundary-line']);
  });

  // A page that stops giving a boundary at all — not an empty one — must still get the drawn one taken
  // off, or the polygon outlives the parcel it stood for.
  it('takes a drawn boundary off when the caller stops giving one', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();
    expect(maps[0].sources.has('boundary')).toBe(true);

    rerender(<MapView {...POSITION} sources={[STREETS]} />);

    expect(maps[0].sources.has('boundary')).toBe(false);
    expect(maps[0].layers).toEqual([]);
  });

  // A corner dragged while an elevation source is still loading arrives at a style that cannot be
  // changed yet. The map settling is when the drawn boundary catches up with the one the reader made.
  it('brings a boundary moved while the style was busy in line once the map settles', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();
    maps[0].styleLoaded = false;
    const moved = [CORNERS[0], { latitude: 41.383, longitude: -70.637 }, CORNERS[2]];
    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={moved} />);
    expect(maps[0].sources.get('boundary')!.setData).not.toHaveBeenCalled();

    act(() => {
      maps[0].styleLoaded = true;
      maps[0].settle();
    });

    expect(ring()[1]).toEqual([-70.637, 41.383]);
  });

  it('draws the boundary as a closed ring over the basemap', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();

    expect(ring()).toHaveLength(CORNERS.length + 1);
    expect(ring()[0]).toEqual([-70.64, 41.38]);
    expect(ring()[CORNERS.length]).toEqual(ring()[0]);
  });

  it('draws it again after a style swap wiped what the style held', () => {
    render(<MapView {...POSITION} sources={[AERIAL, STREETS]} boundary={CORNERS} />);
    theStyleArrives();
    fireEvent.click(screen.getByRole('button', { name: 'Streets' }));
    maps[0].sources.clear();
    maps[0].layers.length = 0;

    act(() => maps[0].fire('styledata'));

    expect(maps[0].sources.has('boundary')).toBe(true);
    expect(maps[0].layers).toContain('boundary-fill');
  });

  it('adds a corner where the map is clicked, when a change handler makes it editable', () => {
    const onBoundaryChange = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={[]} onBoundaryChange={onBoundaryChange} />);

    act(() => maps[0].fire('click', { lngLat: { lat: 41.384, lng: -70.636 } }));

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
    theStyleArrives();
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
    theStyleArrives();
    const corners = draggable();
    expect(corners).toHaveLength(CORNERS.length);
    // Drawn first, or "it was taken off" passes on a boundary that was never put on.
    expect(maps[0].sources.has('boundary')).toBe(true);

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={[]} onBoundaryChange={onBoundaryChange} />);

    corners.forEach((corner) => expect(corner.remove).toHaveBeenCalled());
    expect(maps[0].sources.has('boundary')).toBe(false);
    expect(maps[0].layers).toEqual([]);
  });

  // Bug #6909. A page that opens on the whole world and closes in on a picked position changes the
  // zoom it opens at. That is the zoom the map *opens* at, so it moves the map rather than replacing
  // it — a replaced map is built with no style, and the style is applied when the source changes,
  // which a zoom change is not.
  it('moves rather than rebuilds when the zoom it opens at changes', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} initialZoom={2} />);
    theStyleArrives();

    rerender(<MapView {...POSITION} sources={[STREETS]} initialZoom={16} />);

    expect(maps).toHaveLength(1);
    expect(maps[0].remove).not.toHaveBeenCalled();
    expect(maps[0].setStyle).toHaveBeenCalledWith('https://tiles.example.org/streets');
  });

  // The other half of the same bug: whatever rebuilds the map, the new one must be drawn on. Sources
  // arriving after the first render is the case every public page meets, because the page is drawn
  // before the service has answered what it may draw with.
  it('draws the basemap on a map built after the sources arrive', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[]} />);
    expect(maps).toHaveLength(0);

    rerender(<MapView {...POSITION} sources={[STREETS]} />);

    expect(maps).toHaveLength(1);
    expect(maps[0].setStyle).toHaveBeenCalledWith('https://tiles.example.org/streets');
  });
});

// Feature #6912 — land rather than a diagram, from what the model declares and nothing else. A source
// saying nothing about the ground draws exactly as every source does today, which is what keeps this
// from being a change every deployment has to opt out of.
describe('drawing the land in three dimensions', () => {
  it('raises the ground from the pyramid the model declares, once the style is in', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    expect(maps[0].terrain).toBeNull();

    theStyleArrives();

    expect(maps[0].sources.get('terrain')).toMatchObject({
      type: 'raster-dem',
      tiles: ['https://elevation.example.org/{z}/{x}/{y}.png'],
      encoding: 'terrarium',
    });
    expect(maps[0].terrain).toEqual({ source: 'terrain', exaggeration: 1.4 });
  });

  it('raises the buildings out of the layer the model names', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();

    expect(maps[0].layers).toContain('buildings-raised');
    expect(maps[0].addedLayers.find((layer) => layer.id === 'buildings-raised')).toMatchObject({
      type: 'fill-extrusion',
      'source-layer': 'building',
    });
  });

  // A map that raises land opens showing it, or the reader has been handed a diagram again. Looking
  // straight down is theirs to ask for, and the control below is what says so.
  it('opens tilted where the model declares ground to raise', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();

    const openedAt = maps[0].easeTo.mock.calls.at(-1)![0] as { pitch: number };
    expect(openedAt.pitch).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Look straight down' })).toBeInTheDocument();
  });

  it('takes the ground back down when the reader asks, leaving the map drawn', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();

    fireEvent.click(screen.getByRole('button', { name: 'Look straight down' }));

    expect(maps[0].easeTo).toHaveBeenLastCalledWith(expect.objectContaining({ pitch: 0 }));
    expect(maps[0].sources.has('terrain')).toBe(true);
  });

  it('puts the tilt back when the reader asks for it again', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();
    fireEvent.click(screen.getByRole('button', { name: 'Look straight down' }));

    fireEvent.click(screen.getByRole('button', { name: 'Tilt the view' }));

    const tiltedTo = maps[0].easeTo.mock.calls.at(-1)![0] as { pitch: number };
    expect(tiltedTo.pitch).toBeGreaterThan(0);
  });

  // Nothing declared, nothing raised: no elevation source, no extrusion, a map left flat and no control
  // offering a view the model cannot draw.
  it('draws flat where the model declares no ground', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    theStyleArrives();

    expect(maps[0].sources.has('terrain')).toBe(false);
    expect(maps[0].layers).not.toContain('buildings-raised');
    expect(maps[0].terrain).toBeNull();
    expect(maps[0].easeTo).toHaveBeenLastCalledWith(expect.objectContaining({ pitch: 0 }));
    expect(screen.queryByRole('button', { name: 'Tilt the view' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Look straight down' })).toBeNull();
  });

  // What the live run showed: `styledata` fires while the style is still coming in, so the map answers
  // that it holds nothing and the handler raises nothing. `idle` is the map saying it has drawn what it
  // has, and is where the ground actually goes in.
  it('raises the ground on the first idle where the style arrived before one', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    act(() => {
      maps[0].fire('styledata');
    });
    expect(maps[0].sources.has('terrain')).toBe(false);

    act(() => {
      maps[0].styleLoaded = true;
      maps[0].settle();
    });

    expect(maps[0].sources.has('terrain')).toBe(true);
    expect(maps[0].terrain).toEqual({ source: 'terrain', exaggeration: 1.4 });
  });

  // Raising the ground fires maplibre's `terrain` event, whose own handler repaints the map — which
  // settles into another idle. Raising again on each of those never stops, and every pass destroys and
  // rebuilds the terrain and its render-to-texture cache.
  it('raises the ground once, however often the map settles', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();
    const raisedOnce = maps[0].setTerrain.mock.calls.length;

    act(() => {
      maps[0].settle();
      maps[0].settle();
    });

    expect(maps[0].setTerrain.mock.calls.length).toBe(raisedOnce);
  });

  // The control goes when the reader switches to a source with nothing to raise, so a camera left over
  // is a tilted flat map with no way back to looking down.
  it('takes the camera back down when the reader switches to a source that raises nothing', () => {
    render(<MapView {...POSITION} sources={[RAISED, AERIAL]} />);
    theStyleArrives();

    fireEvent.click(screen.getByRole('button', { name: 'Aerial' }));

    expect(maps[0].easeTo).toHaveBeenLastCalledWith(expect.objectContaining({ pitch: 0 }));
  });
});

// The rest of Feature #6912: the land is drawn on a globe under a sky wherever the library can draw
// one, which is every map this client builds, and the camera's heading is shown and given back.
describe('the globe, the sky and the heading', () => {
  const LIBRARY_PITCH_CEILING = 60;

  it('draws on a globe under a sky once the style is in', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    expect(maps[0].projection).toBeUndefined();
    expect(maps[0].sky).toBeUndefined();

    theStyleArrives();

    expect(maps[0].projection).toEqual({ type: 'globe' });
    expect(maps[0].sky).toMatchObject({ 'sky-color': expect.any(String) });
  });

  // Setting either repaints the map, which settles into another idle. Set on each of those, the map
  // never stops drawing — the trap raising the ground already guards against.
  it('sets the globe and the sky once, however often the map settles', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    theStyleArrives();
    const globeSetOnce = maps[0].setProjection.mock.calls.length;
    const skySetOnce = maps[0].setSky.mock.calls.length;
    expect(globeSetOnce).toBe(1);
    expect(skySetOnce).toBe(1);

    act(() => {
      maps[0].settle();
      maps[0].settle();
    });

    expect(maps[0].setProjection.mock.calls.length).toBe(globeSetOnce);
    expect(maps[0].setSky.mock.calls.length).toBe(skySetOnce);
  });

  it('puts both back on a swapped style, which brought neither', () => {
    render(<MapView {...POSITION} sources={[AERIAL, STREETS]} />);
    theStyleArrives();
    fireEvent.click(screen.getByRole('button', { name: 'Streets' }));
    maps[0].projection = undefined;
    maps[0].sky = undefined;

    act(() => maps[0].fire('styledata'));

    expect(maps[0].projection).toEqual({ type: 'globe' });
    expect(maps[0].sky).toBeDefined();
  });

  // The sky stands above the horizon, and the library's own ceiling on the camera stops short of
  // bringing the horizon into view. Where there is land to look across, the camera may lean far enough.
  it('lets the camera lean to the horizon where the model declares ground to raise', () => {
    render(<MapView {...POSITION} sources={[RAISED]} />);
    theStyleArrives();

    expect(maps[0].maxPitch).toBeGreaterThan(LIBRARY_PITCH_CEILING);
  });

  it('keeps the library ceiling on a flat map, and returns to it on switching to one', () => {
    render(<MapView {...POSITION} sources={[RAISED, AERIAL]} />);
    theStyleArrives();
    expect(maps[0].maxPitch).toBeGreaterThan(LIBRARY_PITCH_CEILING);

    fireEvent.click(screen.getByRole('button', { name: 'Aerial' }));

    expect(maps[0].maxPitch).toBe(LIBRARY_PITCH_CEILING);
  });

  // Turning the map is the library's own gesture. What the page adds is where north has gone, and the
  // way back to it.
  it('shows which way north lies as the reader turns the map', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    const needle = () => screen.getByRole('button', { name: 'North up' }).querySelector('svg')!;
    expect(needle().style.transform).toBe('rotate(0deg)');

    act(() => {
      maps[0].bearing = 30;
      maps[0].fire('rotate');
    });

    expect(needle().style.transform).toBe('rotate(-30deg)');
  });

  it('turns the map back to north when asked', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
    act(() => {
      maps[0].bearing = 30;
      maps[0].fire('rotate');
    });

    fireEvent.click(screen.getByRole('button', { name: 'North up' }));

    expect(maps[0].resetNorth).toHaveBeenCalledTimes(1);
  });
});
