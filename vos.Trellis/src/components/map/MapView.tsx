import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Map as MapLibreMap,
  Marker,
  config as maplibreConfiguration,
  type GeoJSONSource,
  type MapMouseEvent,
  type ProjectionSpecification,
  type SkySpecification,
} from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import { Navigation2 } from 'lucide-react';
import type { Feature } from 'geojson';
import { styleForSource } from '../../utils/basemapSources';
import { useMapStore, resolveSelectedSource } from '../../stores/mapStore';
import type { BasemapSource, RaisedGround } from '../../types/basemap';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

// Naming the worker is what makes the bundler emit it: maplibre's own address for it is computed at
// run time, which a bundler cannot see. Unnamed, no tile is ever parsed and nothing says so — see
// docs/TRELLIS.md §22.
//
// Asked for as a worker rather than as a file, because the library splits it in two and the worker
// imports the other half by name. Asked for as a file, the bundler copies the one it was named and
// nothing it imports, so every built map answered 404 for the half nobody emitted and parsed no tile
// (Bug #6910). `ci/every-worker-carries-its-imports.mjs` fails a build that emits one that way again.
maplibreConfiguration.WORKER_URL = maplibreWorkerUrl;

const DEFAULT_ZOOM = 15;

// A control sits on the map, not on the page, so its colours follow the tiles underneath rather than
// the app's theme — light text on this chip would disappear the moment the reader switched to dark.
const CONTROL_CLASSES = 'rounded bg-white/90 px-2 py-1 text-xs text-zinc-900 shadow';

const BOUNDARY_SOURCE_ID = 'boundary';
const BOUNDARY_COLOUR = '#059669';

const TERRAIN_SOURCE_ID = 'terrain';
const BUILDINGS_LAYER_ID = 'buildings-raised';

/** Elevation pyramids stop well short of the zooms a parcel is looked at, and the map keeps shaping
 *  the ground from the deepest tiles it has rather than asking for addresses that answer nothing. */
const TERRAIN_DEEPEST_ZOOM = 14;

/** Grey enough to read as massing rather than as a model of anybody's house. */
const BUILDING_COLOUR = '#c8ccd4';

/** Far enough over to see the fall of the land, short of the angle where the horizon takes the view. */
const TILTED_PITCH = 55;
const TILT_MILLISECONDS = 700;

/** How far over the camera may lean where there is land to look across: far enough to bring the
 *  horizon, and the sky above it, into view. The library's own ceiling stops short of the horizon. */
const HORIZON_PITCH = 80;

/** A globe from far out and the flat plane every parcel is drawn on from close in — the library's own
 *  blend between the two, by zoom. */
const GLOBE: ProjectionSpecification = { type: 'globe' };

/** Daylight over the land: a sky, a paler band at the horizon, haze on the far ground, and from far
 *  enough out the atmosphere round the globe. */
const SKY: SkySpecification = {
  'sky-color': '#a7cbee',
  'horizon-color': '#e7eff7',
  'fog-color': '#e7eff7',
  'sky-horizon-blend': 0.6,
  'horizon-fog-blend': 0.6,
  'fog-ground-blend': 0.9,
};

interface MapViewProps {
  latitude: number;
  longitude: number;
  /** The basemap sources this model holds. An empty list is a model that declares none. */
  sources: BasemapSource[];
  initialZoom?: number;
  /** Where a moved position flies to, for a map that opens on the whole world and closes in once a
   *  position is picked. A map that opens where it works stays at its one zoom. */
  focusZoom?: number;
  /** A polygon drawn over the basemap. Fewer than three corners enclose nothing and draw nothing,
   *  though each corner still gets its handle while the boundary is editable. */
  boundary?: readonly BoundaryPoint[];
  /** Handed the whole boundary after a click places a corner or a drag moves one. Giving it is what
   *  makes the boundary editable. */
  onBoundaryChange?: (boundary: BoundaryPoint[]) => void;
  /** Handed where a click landed, for a page whose position comes from the map rather than the map
   *  from the position. Boundary editing wins the click: a page offering both is placing corners. */
  onPositionPick?: (position: BoundaryPoint) => void;
  /** False while the position props are a stand-in rather than anywhere anybody chose — the pin, the
   *  recentre control and the coordinate readout would all present the stand-in as an answer. */
  showMarker?: boolean;
}

/**
 * A basemap centred on a point, with whatever layers the model offers.
 *
 * This component knows nothing about the page showing it. It takes a position and a list of
 * sources and returns a map; every caller — the model viewer, the intake wizard — supplies its
 * own position and passes the same sources through.
 */
export function MapView({
  latitude,
  longitude,
  sources,
  initialZoom = DEFAULT_ZOOM,
  focusZoom,
  boundary,
  onBoundaryChange,
  onPositionPick,
  showMarker = true,
}: MapViewProps) {
  const { t } = useTranslation();
  const container = useRef<HTMLDivElement | null>(null);
  const map = useRef<MapLibreMap | null>(null);
  const marker = useRef<Marker | null>(null);
  /** The position the map is showing — and where a map built later starts. */
  const shownAt = useRef<[number, number] | null>(null);
  const selectedSourceName = useMapStore((s) => s.selectedSourceName);
  const tilesUnreachable = useMapStore((s) => s.tilesUnreachable);
  const selectSource = useMapStore((s) => s.selectSource);
  const reportTilesUnreachable = useMapStore((s) => s.reportTilesUnreachable);
  const selected = resolveSelectedSource(sources, selectedSourceName);
  const hasSource = selected !== null;

  // Declared before the map is built, so the first run only records where to build it. After that,
  // a moved position moves the map that exists rather than building a new one — the wizard moves it
  // with every keystroke in a coordinate field, and a rebuild refetches every tile.
  useEffect(() => {
    const [shownLongitude, shownLatitude] = shownAt.current ?? [];
    const moved = shownLongitude !== longitude || shownLatitude !== latitude;
    shownAt.current = [longitude, latitude];
    if (!map.current || !moved) return;
    marker.current?.setLngLat([longitude, latitude]);
    map.current.flyTo({ center: [longitude, latitude], zoom: focusZoom ?? initialZoom });
  }, [latitude, longitude, initialZoom, focusZoom]);

  // The zoom the map opens at, read here rather than watched: it is an opening value, and a caller
  // that changes it — a page opening on the world and closing in on a picked position — means move the
  // map, not replace it. Watched, the rebuilt map came up with the empty style it is built with and
  // the swap below, which watches the source, had no reason to run (Bug #6909).
  const openingZoom = useRef(initialZoom);
  // Kept current ahead of the build below, which is declared after this and so sees the settled value.
  useEffect(() => {
    openingZoom.current = initialZoom;
  }, [initialZoom]);

  // Counted so everything that draws on the map runs again when there is a new one to draw on.
  const [mapGeneration, setMapGeneration] = useState(0);

  useEffect(() => {
    const centre = shownAt.current;
    if (!container.current || !hasSource || !centre) return;
    const created = new MapLibreMap({
      container: container.current,
      center: centre,
      zoom: openingZoom.current,
    });
    created.on('error', reportTilesUnreachable);
    marker.current = new Marker().setLngLat(centre).addTo(created);
    map.current = created;
    setMapGeneration((generation) => generation + 1);
    return () => {
      created.remove();
      map.current = null;
      marker.current = null;
    };
  }, [hasSource, reportTilesUnreachable]);

  // Swapping the style instead of rebuilding the map leaves the reader where they had panned to,
  // which is why the map above is built without one. What was drawn is remembered as the pair it is —
  // this style, on this map — so a source the reader switches to is drawn, and so is a map built after
  // the sources arrived, while a render that moved neither draws nothing.
  const drawn = useRef<{ map: MapLibreMap; source: BasemapSource } | null>(null);
  useEffect(() => {
    const current = map.current;
    if (!current || !selected) return;
    if (drawn.current?.map === current && drawn.current.source === selected) return;
    drawn.current = { map: current, source: selected };
    current.setStyle(styleForSource(selected));
  }, [selected, mapGeneration]);

  // A style swap wipes every source and layer the style did not bring, so the boundary is drawn
  // again on styledata, not only when it changes.
  useEffect(() => {
    const current = map.current;
    if (!current) return;
    // Changing a style maplibre has not finished loading throws, and a throw in an effect takes the
    // whole page down with it. Leaving this step and coming back builds a second map with the
    // boundary already in hand, which is how a draw arrives that early; the styledata below is what
    // draws it once the style is in.
    const redraw = () => {
      if (!current.isStyleLoaded()) return;
      drawBoundary(current, boundary ?? []);
    };
    redraw();
    current.on('styledata', redraw);
    return () => {
      current.off('styledata', redraw);
    };
  }, [boundary, hasSource]);

  // The land itself: on a globe, under a sky, and where the model declares it, raised from the elevation
  // tiles with the buildings the basemap already carries raised out of it. Drawn on styledata for the
  // same reason the boundary is — a style swap wipes everything the style did not bring, all of this
  // included.
  const ground = selected?.terrain;
  const buildingSourceLayer = selected?.buildingSourceLayer;
  useEffect(() => {
    const current = map.current;
    if (!current) return;

    const dress = () => {
      if (!current.isStyleLoaded()) return;
      drawOnAGlobeUnderASky(current);
      if (ground) raiseTheGround(current, ground);
      if (buildingSourceLayer) raiseTheBuildings(current, buildingSourceLayer);
    };
    dress();
    // `styledata` fires while the style is still coming in, so a handler on it alone finds the style
    // unloaded every time and draws nothing. `idle` is the map saying it has drawn everything it
    // has, which is the first moment the style can be added to.
    current.on('styledata', dress);
    current.on('idle', dress);
    return () => {
      current.off('styledata', dress);
      current.off('idle', dress);
    };
  }, [ground, buildingSourceLayer, hasSource, mapGeneration]);

  // A map that raises land opens showing it, because a reader who cannot see the fall of the land has
  // been shown a diagram again; looking straight down is theirs to ask for, and is what somebody
  // drawing a boundary corner by corner works in. Kept here rather than on the map so the control can
  // say which view is on.
  const canTilt = Boolean(ground || buildingSourceLayer);
  const [tiltWanted, setTiltWanted] = useState(true);
  // What the reader wants is remembered, being tilted is not: switching to a source that raises nothing
  // takes the control away with it, and a camera left over is a tilted flat map with nothing to ask for
  // the way back. Switching to one that raises something again returns the view they had.
  const tilted = tiltWanted && canTilt;
  useEffect(() => {
    const current = map.current;
    if (!current) return;
    // Where there is land to look across the camera may lean to the horizon; a flat map keeps the
    // library's own ceiling, and switching to one brings the camera back under it.
    current.setMaxPitch(canTilt ? HORIZON_PITCH : null);
    current.easeTo({ pitch: tilted ? TILTED_PITCH : 0, duration: TILT_MILLISECONDS });
  }, [tilted, canTilt, mapGeneration]);

  // Turning the map is the library's own gesture — a drag with the second mouse button, or two fingers.
  // What the page adds is where north has gone, and the way back to it.
  const [bearing, setBearing] = useState(0);
  useEffect(() => {
    const current = map.current;
    if (!current) return;
    const follow = () => setBearing(current.getBearing());
    follow();
    current.on('rotate', follow);
    return () => {
      current.off('rotate', follow);
    };
  }, [hasSource, mapGeneration]);

  useEffect(() => {
    const current = map.current;
    if (!current || !onBoundaryChange || !boundary) return;
    const corners = boundary.map((corner, held) => {
      const handle = new Marker({ draggable: true, scale: 0.7, color: BOUNDARY_COLOUR })
        .setLngLat([corner.longitude, corner.latitude])
        .addTo(current);
      handle.on('dragend', () => {
        const at = handle.getLngLat();
        onBoundaryChange(
          boundary.map((kept, index) =>
            index === held ? { latitude: at.lat, longitude: at.lng } : kept,
          ),
        );
      });
      return handle;
    });
    return () => corners.forEach((handle) => handle.remove());
  }, [boundary, onBoundaryChange, hasSource]);

  useEffect(() => {
    const current = map.current;
    if (!current || !onBoundaryChange) return;
    const place = (event: MapMouseEvent) =>
      onBoundaryChange([...(boundary ?? []), { latitude: event.lngLat.lat, longitude: event.lngLat.lng }]);
    current.on('click', place);
    return () => {
      current.off('click', place);
    };
  }, [boundary, onBoundaryChange, hasSource]);

  useEffect(() => {
    const current = map.current;
    if (!current || !onPositionPick || onBoundaryChange) return;
    const pick = (event: MapMouseEvent) =>
      onPositionPick({ latitude: event.lngLat.lat, longitude: event.lngLat.lng });
    current.on('click', pick);
    return () => {
      current.off('click', pick);
    };
  }, [onPositionPick, onBoundaryChange, hasSource]);

  // The pin is built with the map, so hiding it is taking it off rather than never making it.
  useEffect(() => {
    const pin = marker.current;
    const current = map.current;
    if (!pin || !current) return;
    pin.remove();
    if (showMarker) pin.addTo(current);
  }, [showMarker, hasSource]);

  const coordinates = t('map.coordinates', {
    latitude: latitude.toFixed(5),
    longitude: longitude.toFixed(5),
  });

  if (!selected) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-2 text-sm">
        <p>{t('map.noSource')}</p>
        <p className="font-mono">{coordinates}</p>
      </div>
    );
  }

  return (
    <div className="relative h-full w-full">
      <div ref={container} className="h-full w-full" />
      {sources.length > 1 && (
        <div className="absolute top-2 right-2 flex flex-col gap-1" role="group" aria-label={t('map.baseLayer')}>
          {sources.map((source) => (
            <button
              key={source.id}
              type="button"
              onClick={() => selectSource(source.name)}
              aria-pressed={source.name === selected.name}
              className={CONTROL_CLASSES}
            >
              {source.name}
            </button>
          ))}
        </div>
      )}
      {showMarker && (
        <button
          type="button"
          onClick={() =>
            map.current?.flyTo({ center: [longitude, latitude], zoom: focusZoom ?? initialZoom })}
          className={`absolute bottom-2 right-2 ${CONTROL_CLASSES}`}
        >
          {t('map.recentre')}
        </button>
      )}
      <div className="absolute top-2 left-2 flex flex-col items-start gap-1">
        {/* Offered only where the model declares something to raise, so the control never promises a
            view this map cannot draw. */}
        {canTilt && (
          <button type="button" onClick={() => setTiltWanted(() => !tilted)} className={CONTROL_CLASSES}>
            {tilted ? t('map.lookDown') : t('map.tilt')}
          </button>
        )}
        <button
          type="button"
          onClick={() => map.current?.resetNorth()}
          className={`flex items-center gap-1 ${CONTROL_CLASSES}`}
        >
          <Navigation2 size={12} aria-hidden="true" style={{ transform: `rotate(${-bearing}deg)` }} />
          {t('map.northUp')}
        </button>
        {tilesUnreachable && (
          <p role="status" className={CONTROL_CLASSES}>
            {t('map.tilesUnreachable')}
          </p>
        )}
      </div>
      {showMarker && (
        <p className={`absolute bottom-2 left-2 font-mono ${CONTROL_CLASSES}`}>{coordinates}</p>
      )}
    </div>
  );
}

// Setting the projection or the sky repaints the map, which settles into the idle this runs on; set
// on every one of those, the map never stops drawing. Each is set once per style.
function drawOnAGlobeUnderASky(map: MapLibreMap): void {
  if (map.getProjection()?.type !== GLOBE.type) map.setProjection(GLOBE);
  if (!map.getSky()) map.setSky(SKY);
}

function raiseTheGround(map: MapLibreMap, ground: NonNullable<RaisedGround['terrain']>): void {
  if (!map.getSource(TERRAIN_SOURCE_ID)) {
    map.addSource(TERRAIN_SOURCE_ID, {
      type: 'raster-dem',
      tiles: [ground.tileUrl],
      tileSize: 256,
      encoding: ground.encoding,
      maxzoom: TERRAIN_DEEPEST_ZOOM,
    });
  }
  // Draping the ground is not a call that can be repeated: maplibre rebuilds the terrain and its
  // render-to-texture cache each time, and fires the event whose own handler repaints the map —
  // which settles into the idle this runs on, and never stops.
  if (!map.getTerrain()) {
    map.setTerrain({ source: TERRAIN_SOURCE_ID, exaggeration: ground.exaggeration });
  }
}

function raiseTheBuildings(map: MapLibreMap, sourceLayer: string): void {
  if (map.getLayer(BUILDINGS_LAYER_ID)) return;
  // Which source inside the style holds them is the style's business, not the model's: the model
  // names the layer, and the first vector source is the one a vector basemap keeps it in.
  const vectorSource = Object.entries(map.getStyle()?.sources ?? {})
    .find(([, source]) => (source as { type?: string }).type === 'vector')?.[0];
  if (!vectorSource) return;
  map.addLayer({
    id: BUILDINGS_LAYER_ID,
    type: 'fill-extrusion',
    source: vectorSource,
    'source-layer': sourceLayer,
    paint: {
      'fill-extrusion-color': BUILDING_COLOUR,
      // What the footprint says it is, or a storey's worth where it says nothing — massing to
      // read the place by, never a claim about anybody's house.
      'fill-extrusion-height': ['coalesce', ['get', 'render_height'], ['get', 'height'], 3],
      'fill-extrusion-base': ['coalesce', ['get', 'render_min_height'], ['get', 'min_height'], 0],
      'fill-extrusion-opacity': 0.85,
    },
  });
}

function drawBoundary(map: MapLibreMap, boundary: readonly BoundaryPoint[]): void {
  const drawn = map.getSource(BOUNDARY_SOURCE_ID) as GeoJSONSource | undefined;
  if (boundary.length < 3) {
    if (drawn) {
      map.removeLayer(`${BOUNDARY_SOURCE_ID}-fill`);
      map.removeLayer(`${BOUNDARY_SOURCE_ID}-line`);
      map.removeSource(BOUNDARY_SOURCE_ID);
    }
    return;
  }

  const ring = [...boundary, boundary[0]].map((corner) => [corner.longitude, corner.latitude]);
  const outline: Feature = {
    type: 'Feature',
    properties: {},
    geometry: { type: 'Polygon', coordinates: [ring] },
  };
  if (drawn) {
    drawn.setData(outline);
    return;
  }
  map.addSource(BOUNDARY_SOURCE_ID, { type: 'geojson', data: outline });
  map.addLayer({
    id: `${BOUNDARY_SOURCE_ID}-fill`,
    type: 'fill',
    source: BOUNDARY_SOURCE_ID,
    paint: { 'fill-color': BOUNDARY_COLOUR, 'fill-opacity': 0.15 },
  });
  map.addLayer({
    id: `${BOUNDARY_SOURCE_ID}-line`,
    type: 'line',
    source: BOUNDARY_SOURCE_ID,
    paint: { 'line-color': BOUNDARY_COLOUR, 'line-width': 2 },
  });
}
