import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Map as MapLibreMap, Marker, type GeoJSONSource, type MapMouseEvent } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import type { Feature } from 'geojson';
import { styleForSource } from '../../utils/basemapSources';
import { useMapStore, resolveSelectedSource } from '../../stores/mapStore';
import type { BasemapSource } from '../../types/basemap';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

const DEFAULT_ZOOM = 15;

// A control sits on the map, not on the page, so its colours follow the tiles underneath rather than
// the app's theme — light text on this chip would disappear the moment the reader switched to dark.
const CONTROL_CLASSES = 'rounded bg-white/90 px-2 py-1 text-xs text-zinc-900 shadow';

const BOUNDARY_SOURCE_ID = 'boundary';
const BOUNDARY_COLOUR = '#059669';

interface MapViewProps {
  latitude: number;
  longitude: number;
  /** The basemap sources this model holds. An empty list is a model that declares none. */
  sources: BasemapSource[];
  initialZoom?: number;
  /** A polygon drawn over the basemap. Fewer than three corners enclose nothing and draw nothing,
   *  though each corner still gets its handle while the boundary is editable. */
  boundary?: readonly BoundaryPoint[];
  /** Handed the whole boundary after a click places a corner or a drag moves one. Giving it is what
   *  makes the boundary editable. */
  onBoundaryChange?: (boundary: BoundaryPoint[]) => void;
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
  boundary,
  onBoundaryChange,
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
    map.current.flyTo({ center: [longitude, latitude], zoom: initialZoom });
  }, [latitude, longitude, initialZoom]);

  useEffect(() => {
    const centre = shownAt.current;
    if (!container.current || !hasSource || !centre) return;
    const created = new MapLibreMap({
      container: container.current,
      center: centre,
      zoom: initialZoom,
    });
    created.on('error', reportTilesUnreachable);
    marker.current = new Marker().setLngLat(centre).addTo(created);
    map.current = created;
    return () => {
      created.remove();
      map.current = null;
      marker.current = null;
    };
  }, [initialZoom, hasSource, reportTilesUnreachable]);

  // Swapping the style instead of rebuilding the map leaves the reader where they had panned to,
  // which is why the map above is built without one.
  useEffect(() => {
    if (selected) map.current?.setStyle(styleForSource(selected));
  }, [selected]);

  // A style swap wipes every source and layer the style did not bring, so the boundary is drawn
  // again on styledata, not only when it changes.
  useEffect(() => {
    const current = map.current;
    if (!current) return;
    const redraw = () => drawBoundary(current, boundary ?? []);
    redraw();
    current.on('styledata', redraw);
    return () => {
      current.off('styledata', redraw);
    };
  }, [boundary, hasSource]);

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
      <button
        type="button"
        onClick={() => map.current?.flyTo({ center: [longitude, latitude], zoom: initialZoom })}
        className={`absolute bottom-2 right-2 ${CONTROL_CLASSES}`}
      >
        {t('map.recentre')}
      </button>
      <p className={`absolute bottom-2 left-2 font-mono ${CONTROL_CLASSES}`}>{coordinates}</p>
      {tilesUnreachable && (
        <p role="status" className={`absolute top-2 left-2 ${CONTROL_CLASSES}`}>
          {t('map.tilesUnreachable')}
        </p>
      )}
    </div>
  );
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
