import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Map as MapLibreMap, Marker } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { styleForSource } from '../../api/basemapApi';
import { useMapStore, resolveSelectedSource } from '../../stores/mapStore';
import type { BasemapSource } from '../../types/basemap';

const DEFAULT_ZOOM = 15;

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

  useEffect(() => {
    if (!container.current || !selected) return;
    const created = new MapLibreMap({
      container: container.current,
      style: styleForSource(selected),
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
  }, [latitude, longitude, initialZoom, selected, reportTilesUnreachable]);

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
      <div ref={container} className="h-full w-full" data-testid="map-canvas" />
      {sources.length > 1 && (
        <div className="absolute top-2 right-2 flex flex-col gap-1" role="group" aria-label={t('map.baseLayer')}>
          {sources.map((source) => (
            <button
              key={source.id}
              type="button"
              onClick={() => selectSource(source.name)}
              aria-pressed={source.name === selected.name}
              className="rounded bg-white/90 px-2 py-1 text-xs shadow"
            >
              {source.name}
            </button>
          ))}
        </div>
      )}
      <button
        type="button"
        onClick={() => map.current?.flyTo({ center: [longitude, latitude], zoom: initialZoom })}
        className="absolute bottom-2 right-2 rounded bg-white/90 px-2 py-1 text-xs shadow"
      >
        {t('map.recentre')}
      </button>
      <p className="absolute bottom-2 left-2 rounded bg-white/90 px-2 py-1 font-mono text-xs shadow">
        {coordinates}
      </p>
      {tilesUnreachable && (
        <p role="status" className="absolute top-2 left-2 rounded bg-white/90 px-2 py-1 text-xs shadow">
          {t('map.tilesUnreachable')}
        </p>
      )}
    </div>
  );
}
