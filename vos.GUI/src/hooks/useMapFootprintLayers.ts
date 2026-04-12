import { useEffect, useMemo } from 'react';
import type { Map as MaplibreMap } from 'maplibre-gl';
import type { VosThing } from '../types/vos';
import { useUiStore } from '../stores/uiStore';
import { hashStringToIndex, INSTANCE_PALETTE } from '../utils/colors';

const FOOTPRINT_SOURCE = 'building-footprints';
const FOOTPRINT_FILL_LAYER = 'building-footprints-fill';
const FOOTPRINT_LINE_LAYER = 'building-footprints-line';
const FOOTPRINT_EXTRUSION_LAYER = 'building-footprints-extrusion';

export { FOOTPRINT_SOURCE, FOOTPRINT_FILL_LAYER, FOOTPRINT_LINE_LAYER, FOOTPRINT_EXTRUSION_LAYER };

/**
 * Manages GeoJSON footprint layers (fill, outline, 3D extrusion) on the MapLibre map.
 * Rebuilds when the footprint data changes. Layer visibility controlled by the 3D toggle.
 */
export function useMapFootprintLayers(
  map: MaplibreMap | null,
  mapEnabled: boolean,
  things: VosThing[],
) {
  const footprintFingerprint = useMemo(
    () =>
      things
        .filter((t) => typeof t.Properties?.footprint === 'string')
        .map((t) => `${t.Id}:${(t.Properties.footprint as string).length}`)
        .join('|'),
    [things],
  );

  useEffect(() => {
    if (!mapEnabled || !map) return;

    function addFootprints() {
      if (!map) return;
      const features: GeoJSON.Feature[] = [];
      for (const t of things) {
        const fpProp = t.Properties?.footprint;
        if (typeof fpProp === 'string') {
          try {
            const geometry = JSON.parse(fpProp) as GeoJSON.Geometry;
            const color = INSTANCE_PALETTE[hashStringToIndex(t.Name, INSTANCE_PALETTE.length)];
            features.push({
              type: 'Feature',
              properties: {
                name: t.Name,
                height: typeof t.Properties?.height === 'number' ? t.Properties.height : 10,
                color,
              },
              geometry,
            });
          } catch {
            // Skip malformed footprint JSON
          }
        }
      }

      const geojson: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features };

      if (map.getLayer(FOOTPRINT_EXTRUSION_LAYER)) map.removeLayer(FOOTPRINT_EXTRUSION_LAYER);
      if (map.getLayer(FOOTPRINT_FILL_LAYER)) map.removeLayer(FOOTPRINT_FILL_LAYER);
      if (map.getLayer(FOOTPRINT_LINE_LAYER)) map.removeLayer(FOOTPRINT_LINE_LAYER);
      if (map.getSource(FOOTPRINT_SOURCE)) map.removeSource(FOOTPRINT_SOURCE);

      const is3D = useUiStore.getState().threeDEnabled;

      map.addSource(FOOTPRINT_SOURCE, { type: 'geojson', data: geojson });

      map.addLayer({
        id: FOOTPRINT_FILL_LAYER,
        type: 'fill',
        source: FOOTPRINT_SOURCE,
        paint: { 'fill-color': ['get', 'color'], 'fill-opacity': 0.08 },
        layout: { visibility: is3D ? 'none' : 'visible' },
      });

      map.addLayer({
        id: FOOTPRINT_LINE_LAYER,
        type: 'line',
        source: FOOTPRINT_SOURCE,
        paint: { 'line-color': ['get', 'color'], 'line-width': 1, 'line-opacity': 0.5 },
        layout: { visibility: is3D ? 'none' : 'visible' },
      });

      map.addLayer({
        id: FOOTPRINT_EXTRUSION_LAYER,
        type: 'fill-extrusion',
        source: FOOTPRINT_SOURCE,
        paint: {
          'fill-extrusion-color': ['get', 'color'],
          'fill-extrusion-height': ['get', 'height'],
          'fill-extrusion-base': 0,
          'fill-extrusion-opacity': 0.75,
        },
        layout: { visibility: is3D ? 'visible' : 'none' },
      });
    }

    if (map.loaded()) {
      addFootprints();
    } else {
      map.once('load', addFootprints);
    }
  }, [mapEnabled, footprintFingerprint, map]); // eslint-disable-line react-hooks/exhaustive-deps
}
