import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Map as MapLibreMap, Marker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { styleForSource } from '../../api/basemapApi';
import { useMapStore, resolveSelectedSource } from '../../stores/mapStore';
import type { BasemapSource } from '../../types/basemap';

const DEFAULT_ZOOM = 15;

// A control sits on the map, not on the page, so its colours follow the tiles underneath rather than
// the app's theme — light text on this chip would disappear the moment the reader switched to dark.
const CONTROL_CLASSES = 'rounded bg-white/90 px-2 py-1 text-xs text-zinc-900 shadow';

interface MapViewProps {
  latitude: number;
  longitude: number;
  /** The basemap sources this model holds. An empty list is a model that declares none. */
  sources: BasemapSource[];
  initialZoom?: number;
}

/**
 * A basemap centred on a point, with whatever layers the model offers.
 *
 * This component knows nothing about the page showing it. It takes a position and a list of
 * sources and returns a map; every caller — the model viewer, the intake wizard — supplies its
 * own position and passes the same sources through.
 */
export function MapView({ latitude, longitude, sources, initialZoom = DEFAULT_ZOOM }: MapViewProps) {
  const { t } = useTranslation();
  const container = useRef<HTMLDivElement | null>(null);
  const map = useRef<MapLibreMap | null>(null);
  const selectedSourceName = useMapStore((s) => s.selectedSourceName);
  const tilesUnreachable = useMapStore((s) => s.tilesUnreachable);
  const selectSource = useMapStore((s) => s.selectSource);
  const reportTilesUnreachable = useMapStore((s) => s.reportTilesUnreachable);
  const selected = resolveSelectedSource(sources, selectedSourceName);
  const hasSource = selected !== null;

  useEffect(() => {
    if (!container.current || !hasSource) return;
    const created = new MapLibreMap({
      container: container.current,
      center: [longitude, latitude],
      zoom: initialZoom,
    });
    created.on('error', reportTilesUnreachable);
    new Marker().setLngLat([longitude, latitude]).addTo(created);
    map.current = created;
    return () => {
      created.remove();
      map.current = null;
    };
  }, [latitude, longitude, initialZoom, hasSource, reportTilesUnreachable]);

  // Swapping the style instead of rebuilding the map leaves the reader where they had panned to,
  // which is why the map above is built without one.
  useEffect(() => {
    if (selected) map.current?.setStyle(styleForSource(selected));
  }, [selected]);

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
