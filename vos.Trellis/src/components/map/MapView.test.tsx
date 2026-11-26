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
  zoom: number;
}

interface RecordedMarker {
  options: Record<string, unknown>;
  position: [number, number] | null;
  handlers: Map<string, (event?: unknown) => void>;
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
    /** A handler on one layer's features is held as `click@layer`, which is how a test fires it. */
    on(event: string, layerOrHandler: string | ((event?: unknown) => void), layerHandler?: (event?: unknown) => void) {
      const key = typeof layerOrHandler === 'string' ? `${event}@${layerOrHandler}` : event;
      const handler = typeof layerOrHandler === 'string' ? layerHandler! : layerOrHandler;
      const held = this.handlers.get(key) ?? new Set<(event?: unknown) => void>();
      held.add(handler);
      this.handlers.set(key, held);
    }
    off(event: string, layerOrHandler: string | ((event?: unknown) => void), layerHandler?: (event?: unknown) => void) {
      const key = typeof layerOrHandler === 'string' ? `${event}@${layerOrHandler}` : event;
      const handler = typeof layerOrHandler === 'string' ? layerHandler! : layerOrHandler;
      const held = this.handlers.get(key);
      held?.delete(handler);
      if (held?.size === 0) this.handlers.delete(key);
    }
    zoom = 5;
    getZoom() {
      return this.zoom;
    }
    bounds = { south: 23, west: 46, north: 25, east: 48 };
    getBounds() {
      return {
        getSouth: () => this.bounds.south,
        getWest: () => this.bounds.west,
        getNorth: () => this.bounds.north,
        getEast: () => this.bounds.east,
      };
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
    handlers = new Map<string, (event?: unknown) => void>();
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
    on(event: string, handler: (event?: unknown) => void) {
      this.handlers.set(event, handler);
    }
  }
  const configuration = { WORKER_URL: '' };
  return { mapInstances, markerInstances, markerPositions, MockMap, MockMarker, configuration };
});

vi.mock('maplibre-gl', () => ({
  Map: mocks.MockMap,
  Marker: mocks.MockMarker,
  config: mocks.configuration,
}));
vi.mock('maplibre-gl/dist/maplibre-gl.css', () => ({}));
vi.mock('maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url', () => ({
  default: 'https://example.test/assets/maplibre-worker.mjs',
}));

import { MapView } from './MapView';
import { OwnServerRequestHeaders } from './ownServerRequests';
import { useMapStore } from '../../stores/mapStore';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

const maps = mocks.mapInstances as RecordedMap[];
const markers = mocks.markerInstances as RecordedMarker[];
const markerPositions = mocks.markerPositions;

/** Handles are built again for each new map, the first one included, so only those not taken off are on it. */
const onTheMap = (marker: RecordedMarker) => marker.remove.mock.calls.length === 0;
const draggable = () => markers.filter((marker) => marker.options.draggable === true && onTheMap(marker));

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

  it('sends the sign-in its page provides with a request for the page’s own server', () => {
    render(
      <OwnServerRequestHeaders.Provider value={() => ({ Authorization: 'Bearer the-token' })}>
        <MapView {...POSITION} sources={[STREETS]} />
      </OwnServerRequestHeaders.Provider>,
    );

    const transform = maps[0].options.transformRequest as (url: string) => { headers?: unknown } | undefined;
    expect(transform('/basemaps/satellite-tiles/3/4/5')?.headers).toEqual({ Authorization: 'Bearer the-token' });
  });

  it('sends nothing extra from a page nobody signed in to', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);

    const transform = maps[0].options.transformRequest as (url: string) => unknown;
    expect(transform('/basemaps/satellite-tiles/3/4/5')).toBeUndefined();
  });

  // A provider's terms can require its credit on the map whenever its imagery is shown, and in a screenshot of it.
  it('shows the credit in full at every width, and never folds it away when the map is dragged', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);

    expect(maps[0].options.attributionControl).toEqual({ compact: false });
  });

  // Lose this and the map still mounts, still draws its controls and still reports no error, but never parses a
  // tile. The reason is in MapView.
  it('points maplibre at the worker the bundler emitted', () => {
    expect(mocks.configuration.WORKER_URL).toBe('https://example.test/assets/maplibre-worker.mjs');
  });

  it('says so and keeps the coordinates readable when the model declares no source', () => {
    render(<MapView {...POSITION} sources={[]} />);
    expect(screen.getByText('This model defines no basemap source.')).toBeTruthy();
    expect(screen.getByText('41.38000, -70.64000')).toBeTruthy();
    expect(maps).toHaveLength(0);
  });

  it('opens the map on the given point with the style the model supplied', () => {
    render(<MapView {...POSITION} sources={[STREETS]} />);
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

  it('waits for the style rather than throwing when it opens with a boundary already drawn', () => {
    expect(() =>
      render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />),
    ).not.toThrow();
    expect(maps[0].sources.has('boundary')).toBe(false);

    theStyleArrives();

    expect(maps[0].sources.has('boundary')).toBe(true);
  });

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

  // A page that stops giving a boundary at all, rather than giving an empty one, still has the drawn one taken
  // off, or the polygon stays on the map after the parcel it stood for has gone.
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


  it('redraws a moved boundary in place rather than adding a second one', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();
    const moved = [CORNERS[0], { latitude: 41.383, longitude: -70.637 }, CORNERS[2]];

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={moved} />);

    expect(maps[0].sources.get('boundary')!.setData).toHaveBeenCalled();
    expect(ring()[1]).toEqual([-70.637, 41.383]);
    expect(maps[0].layers).toEqual(['boundary-fill', 'boundary-line']);
  });

  it('offers no drawing at all without a way to report an edit', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);

    expect(maps[0].handlers.has('click')).toBe(false);
    expect(markers.filter((marker) => marker.options.className !== undefined)).toHaveLength(0);
  });

  it('takes a cleared boundary off the map, corners and outline both', () => {
    const editing = { placesCorners: true, movesCorners: true, onEdit: vi.fn() };
    const { rerender } = render(
      <MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing} />,
    );
    theStyleArrives();
    const corners = draggable();
    expect(corners).toHaveLength(CORNERS.length * 2);
    // Drawn first, or "it was taken off" passes on a boundary that was never put on.
    expect(maps[0].sources.has('boundary')).toBe(true);

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={[]} borderEditing={editing} />);

    corners.forEach((corner) => expect(corner.remove).toHaveBeenCalled());
    expect(maps[0].sources.has('boundary')).toBe(false);
    expect(maps[0].layers).toEqual([]);
  });

  it('moves rather than rebuilds when the zoom it opens at changes', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} initialZoom={2} />);
    theStyleArrives();

    rerender(<MapView {...POSITION} sources={[STREETS]} initialZoom={16} />);

    expect(maps).toHaveLength(1);
    expect(maps[0].remove).not.toHaveBeenCalled();
    expect(maps[0].setStyle).toHaveBeenCalledWith('https://tiles.example.org/streets');
  });

  // Every public page meets this, because the page is drawn before the service has answered what it may draw
  // with.
  it('draws the basemap on a map built after the sources arrive', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[]} />);
    expect(maps).toHaveLength(0);

    rerender(<MapView {...POSITION} sources={[STREETS]} />);

    expect(maps).toHaveLength(1);
    expect(maps[0].setStyle).toHaveBeenCalledWith('https://tiles.example.org/streets');
  });
});

describe('editing the border', () => {
  const CORNERS: readonly BoundaryPoint[] = [
    { latitude: 41.38, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.638 },
  ];
  const corners = () => markers.filter((marker) => marker.options.className === 'border-corner' && onTheMap(marker));
  const midpoints = () => markers.filter((marker) => marker.options.className === 'border-midpoint' && onTheMap(marker));
  const pressed = () => ({ originalEvent: { stopPropagation: vi.fn() } });

  function drawing(onEdit = vi.fn()) {
    return { placesCorners: true, movesCorners: false, onEdit };
  }

  function editing(onEdit = vi.fn()) {
    return { placesCorners: false, movesCorners: true, onEdit };
  }

  it('places a corner where the map is clicked while the border places corners', () => {
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={[]} borderEditing={drawing(onEdit)} />);

    act(() => maps[0].fire('click', { lngLat: { lat: 41.384, lng: -70.636 } }));

    expect(onEdit).toHaveBeenCalledWith({ kind: 'place', corner: { latitude: 41.384, longitude: -70.636 } });
  });

  it('shows each corner placed so far, none of them draggable', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS.slice(0, 2)} borderEditing={drawing()} />);

    expect(corners().map((corner) => corner.position)).toEqual([[-70.64, 41.38], [-70.64, 41.382]]);
    expect(corners().every((corner) => corner.options.draggable === false)).toBe(true);
    expect(midpoints()).toHaveLength(0);
  });

  it('closes the border on a press of the first corner, and the press places no corner', () => {
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={drawing(onEdit)} />);
    const press = pressed();

    act(() => corners()[0].handlers.get('click')!(press));

    expect(onEdit).toHaveBeenCalledWith({ kind: 'close' });
    expect(press.originalEvent.stopPropagation).toHaveBeenCalled();
  });

  it('leaves a press of the first corner to the map while there are too few corners to close', () => {
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS.slice(0, 2)} borderEditing={drawing(onEdit)} />);
    const press = pressed();

    act(() => corners()[0].handlers.get('click')?.(press));

    expect(onEdit).not.toHaveBeenCalled();
    expect(press.originalEvent.stopPropagation).not.toHaveBeenCalled();
  });

  it('hands a click to a page picking its position while no border places corners', () => {
    const onPositionPick = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing()} onPositionPick={onPositionPick} />);

    act(() => maps[0].fire('click', { lngLat: { lat: 41.384, lng: -70.636 } }));

    expect(onPositionPick).toHaveBeenCalledWith({ latitude: 41.384, longitude: -70.636 });
  });

  it('gives a click to the corners, not the position, while the border places corners', () => {
    const onPositionPick = vi.fn();
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={[]} borderEditing={drawing(onEdit)} onPositionPick={onPositionPick} />);

    act(() => maps[0].fire('click', { lngLat: { lat: 41.384, lng: -70.636 } }));

    expect(onEdit).toHaveBeenCalledTimes(1);
    expect(onPositionPick).not.toHaveBeenCalled();
  });

  it('places nothing on a click while the border only moves corners', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing()} />);

    expect(maps[0].handlers.has('click')).toBe(false);
  });

  it('reports a dragged corner as moved, and puts its handle back until the page takes the move', () => {
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing(onEdit)} />);
    const corner = corners()[1];
    expect(corner.options.draggable).toBe(true);

    corner.position = [-70.637, 41.383];
    act(() => corner.handlers.get('dragend')!());

    expect(onEdit).toHaveBeenCalledWith({ kind: 'move', index: 1, to: { latitude: 41.383, longitude: -70.637 } });
    expect(corner.position).toEqual([-70.64, 41.382]);
  });

  it('offers a handle at the middle of each side, the closing side last', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing()} />);

    const expected = [
      [-70.64, 41.381],
      [-70.639, 41.382],
      [-70.639, 41.381],
    ];
    expect(midpoints()).toHaveLength(expected.length);
    midpoints().forEach((midpoint, side) => {
      expect(midpoint.position![0]).toBeCloseTo(expected[side][0], 9);
      expect(midpoint.position![1]).toBeCloseTo(expected[side][1], 9);
    });
  });

  it('reports a side’s handle dragged out as a corner on that side, and puts the handle back', () => {
    const onEdit = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={editing(onEdit)} />);
    const midpoint = midpoints()[2];
    const [longitude, latitude] = midpoint.position!;

    midpoint.position = [-70.6385, 41.3805];
    act(() => midpoint.handlers.get('dragend')!());

    expect(onEdit).toHaveBeenCalledWith({ kind: 'insert', side: 2, corner: { latitude: 41.3805, longitude: -70.6385 } });
    expect(midpoint.position![0]).toBeCloseTo(longitude, 9);
    expect(midpoint.position![1]).toBeCloseTo(latitude, 9);
  });

  it('takes the old handles off when the page takes an edit', () => {
    const borderEditing = editing();
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} borderEditing={borderEditing} />);
    const before = [...corners(), ...midpoints()];

    rerender(
      <MapView {...POSITION} sources={[STREETS]} boundary={[...CORNERS, { latitude: 41.38, longitude: -70.638 }]} borderEditing={borderEditing} />,
    );

    before.forEach((handle) => expect(handle.remove).toHaveBeenCalled());
    expect(corners()).toHaveLength(4);
  });
});

describe('the label on the border', () => {
  const CORNERS: readonly BoundaryPoint[] = [
    { latitude: 41.38, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.638 },
  ];
  const labels = () => markers.filter((marker) => marker.options.element !== undefined && onTheMap(marker));

  it('sits at the middle of a border that encloses land, in the outline’s colour', () => {
    render(
      <MapView
        {...POSITION}
        sources={[STREETS]}
        boundary={CORNERS}
        boundaryLabel="North field · 10.1 ha"
        borderColours={{ outline: '#a9413d', fill: '#f08a24', fillOpacity: 0.45 }}
      />,
    );

    expect(labels()).toHaveLength(1);
    const element = labels()[0].options.element as HTMLElement;
    expect(element.textContent).toBe('North field · 10.1 ha');
    expect(element.style.color).toBe('rgb(169, 65, 61)');
    expect(labels()[0].position![0]).toBeCloseTo(-70.639, 9);
    expect(labels()[0].position![1]).toBeCloseTo(41.381, 9);
  });

  it('is not drawn without a label, or without a border that encloses land', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    expect(labels()).toHaveLength(0);

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS.slice(0, 2)} boundaryLabel="North field" />);

    expect(labels()).toHaveLength(0);
  });

  it('is taken off with the border', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} boundaryLabel="North field" />);
    const drawn = labels()[0];

    rerender(<MapView {...POSITION} sources={[STREETS]} boundary={[]} boundaryLabel="North field" />);

    expect(drawn.remove).toHaveBeenCalled();
    expect(labels()).toHaveLength(0);
  });
});

describe('the colours of the border', () => {
  const CORNERS: readonly BoundaryPoint[] = [
    { latitude: 41.38, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.64 },
    { latitude: 41.382, longitude: -70.638 },
  ];
  const paintOf = (id: string) => maps[0].addedLayers.find((layer) => layer.id === id)!.paint;

  it('draws the border in the colours the page gives', () => {
    render(
      <MapView
        {...POSITION}
        sources={[STREETS]}
        boundary={CORNERS}
        borderColours={{ outline: '#a9413d', fill: '#f08a24', fillOpacity: 0.45 }}
        borderEditing={{ placesCorners: true, movesCorners: false, onEdit: vi.fn() }}
      />,
    );
    theStyleArrives();

    expect(paintOf('boundary-line')).toMatchObject({ 'line-color': '#a9413d' });
    expect(paintOf('boundary-fill')).toMatchObject({ 'fill-color': '#f08a24', 'fill-opacity': 0.45 });
    expect(markers.filter((marker) => marker.options.className === 'border-corner' && onTheMap(marker))[0].options.color).toBe('#a9413d');
  });

  it('draws it in the console’s green where the page gives none', () => {
    render(<MapView {...POSITION} sources={[STREETS]} boundary={CORNERS} />);
    theStyleArrives();

    expect(paintOf('boundary-line')).toMatchObject({ 'line-color': '#059669' });
    expect(paintOf('boundary-fill')).toMatchObject({ 'fill-color': '#059669', 'fill-opacity': 0.15 });
  });
});

describe('an approximate area without a border', () => {
  it('draws a dashed circle of the area round the position', () => {
    render(<MapView {...POSITION} sources={[STREETS]} approximateHectares={200} />);
    theStyleArrives();

    const circle = maps[0].sources.get('approximate-area')!.data as { geometry: { coordinates: [number, number][][] } };
    const ring = circle.geometry.coordinates[0];
    expect(ring[0]).toEqual(ring[ring.length - 1]);
    expect(maps[0].addedLayers.find((layer) => layer.id === 'approximate-area-line')!.paint).toHaveProperty('line-dasharray');
  });

  it('takes the circle off once a border encloses land', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} approximateHectares={200} />);
    theStyleArrives();
    expect(maps[0].sources.has('approximate-area')).toBe(true);

    rerender(
      <MapView
        {...POSITION}
        sources={[STREETS]}
        approximateHectares={200}
        boundary={[
          { latitude: 41.38, longitude: -70.64 },
          { latitude: 41.382, longitude: -70.64 },
          { latitude: 41.382, longitude: -70.638 },
        ]}
      />,
    );

    expect(maps[0].sources.has('approximate-area')).toBe(false);
    expect(maps[0].sources.has('boundary')).toBe(true);
  });
});

describe('plots from a register', () => {
  const PLOTS: readonly (readonly BoundaryPoint[])[] = [
    [
      { latitude: 41.38, longitude: -70.64 },
      { latitude: 41.381, longitude: -70.64 },
      { latitude: 41.381, longitude: -70.639 },
    ],
    [
      { latitude: 41.382, longitude: -70.64 },
      { latitude: 41.383, longitude: -70.64 },
      { latitude: 41.383, longitude: -70.639 },
    ],
  ];
  type Drawn = { features: { properties: { index: number; chosen: boolean } }[] };
  const drawnPlots = () => maps[0].sources.get('plots')!.data as Drawn;

  it('draws each plot, marking the one chosen', () => {
    render(<MapView {...POSITION} sources={[STREETS]} plots={PLOTS} chosenPlot={1} onPlotPress={vi.fn()} />);
    theStyleArrives();

    expect(drawnPlots().features.map((feature) => feature.properties)).toEqual([
      { index: 0, chosen: false },
      { index: 1, chosen: true },
    ]);
  });

  it('reports which plot was pressed', () => {
    const onPlotPress = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} plots={PLOTS} onPlotPress={onPlotPress} />);
    theStyleArrives();

    act(() => maps[0].fire('click@plots-fill', { features: [{ properties: { index: 1 } }] }));

    expect(onPlotPress).toHaveBeenCalledWith(1);
  });

  it('takes the plots off when the page stops giving them', () => {
    const { rerender } = render(<MapView {...POSITION} sources={[STREETS]} plots={PLOTS} onPlotPress={vi.fn()} />);
    theStyleArrives();
    expect(maps[0].sources.has('plots')).toBe(true);

    rerender(<MapView {...POSITION} sources={[STREETS]} />);

    expect(maps[0].sources.has('plots')).toBe(false);
    expect(maps[0].handlers.has('click@plots-fill')).toBe(false);
  });
});

describe('where the map has come to rest', () => {
  it('reports the area in view and the zoom each time the map stops moving', () => {
    const onViewSettle = vi.fn();
    render(<MapView {...POSITION} sources={[STREETS]} onViewSettle={onViewSettle} />);
    maps[0].zoom = 15.5;

    act(() => maps[0].fire('moveend'));

    expect(onViewSettle).toHaveBeenCalledWith(
      { southLatitude: 23, westLongitude: 46, northLatitude: 25, eastLongitude: 48 },
      15.5,
    );
  });
});

// The land is raised from what the model declares and nothing else, so a source that says nothing about the
// ground draws flat and no deployment has to opt out.
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

  it('takes the camera back down when the reader switches to a source that raises nothing', () => {
    render(<MapView {...POSITION} sources={[RAISED, AERIAL]} />);
    theStyleArrives();

    fireEvent.click(screen.getByRole('button', { name: 'Aerial' }));

    expect(maps[0].easeTo).toHaveBeenLastCalledWith(expect.objectContaining({ pitch: 0 }));
  });
});

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
