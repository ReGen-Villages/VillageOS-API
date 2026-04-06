import { useEffect } from 'react';
import type { VosThing } from '../types/vos';
import { parseFootprint } from '../utils/geometryDispatcher';
import { hashStringToIndex, INSTANCE_PALETTE } from '../utils/colors';
import { useUiStore } from '../stores/uiStore';

const FOOTPRINT_SOURCE = 'building-footprints';
const FOOTPRINT_FILL_LAYER = 'building-footprints-fill';
const FOOTPRINT_LINE_LAYER = 'building-footprints-line';
const FOOTPRINT_EXTRUSION_LAYER = 'building-footprints-extrusion';

export { FOOTPRINT_FILL_LAYER, FOOTPRINT_LINE_LAYER, FOOTPRINT_EXTRUSION_LAYER };

interface MapBinding {
  map: import('maplibre-gl').Map;
}

/**
 * Manages GeoJSON footprint layers on the MapLibre map.
 * Collects footprints from things with geometry and adds fill, line, and extrusion layers.
 */
export function useFootprintLayers(
  mapEnabled: boolean,
  things: VosThing[],
  geoFingerprint: string,
  bindingRef: React.RefObject<MapBinding | null>,
) {
  useEffect(() => {
    const binding = bindingRef.current;
    if (!mapEnabled || !binding) return;

    const map = binding.map;

    function addFootprints() {
      const features: GeoJSON.Feature[] = [];
      for (const t of things) {
        const geoProp = t.Properties?.geometry;
        if (geoProp != null) {
          const color = INSTANCE_PALETTE[hashStringToIndex(t.Name, INSTANCE_PALETTE.length)];
          const feature = parseFootprint(geoProp, t.Name, color);
          if (feature) features.push(feature);
        }
      }

      const geojson: GeoJSON.FeatureCollection = {
        type: 'FeatureCollection',
        features,
      };

      // Remove existing layers/source if present (data reload)
      if (map.getLayer(FOOTPRINT_EXTRUSION_LAYER)) map.removeLayer(FOOTPRINT_EXTRUSION_LAYER);
      if (map.getLayer(FOOTPRINT_FILL_LAYER)) map.removeLayer(FOOTPRINT_FILL_LAYER);
      if (map.getLayer(FOOTPRINT_LINE_LAYER)) map.removeLayer(FOOTPRINT_LINE_LAYER);
      if (map.getSource(FOOTPRINT_SOURCE)) map.removeSource(FOOTPRINT_SOURCE);

      // Read current 3D state to set correct initial visibility.
      // This handles the race where the 3D toggle effect runs before the map
      // has loaded (and thus before these layers exist). Reading from the store
      // at creation time ensures layers start with the right visibility even
      // when created asynchronously via map.once('load').
      const is3D = useUiStore.getState().threeDEnabled;

      map.addSource(FOOTPRINT_SOURCE, {
        type: 'geojson',
        data: geojson,
      });

      map.addLayer({
        id: FOOTPRINT_FILL_LAYER,
        type: 'fill',
        source: FOOTPRINT_SOURCE,
        paint: {
          'fill-color': ['get', 'color'],
          'fill-opacity': 0.08,
        },
        layout: {
          visibility: is3D ? 'none' : 'visible',
        },
      });

      map.addLayer({
        id: FOOTPRINT_LINE_LAYER,
        type: 'line',
        source: FOOTPRINT_SOURCE,
        paint: {
          'line-color': ['get', 'color'],
          'line-width': 1,
          'line-opacity': 0.5,
        },
        layout: {
          visibility: is3D ? 'none' : 'visible',
        },
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
        layout: {
          visibility: is3D ? 'visible' : 'none',
        },
      });
    }

    if (map.loaded()) {
      addFootprints();
    } else {
      map.once('load', addFootprints);
    }
  }, [mapEnabled, geoFingerprint]); // eslint-disable-line react-hooks/exhaustive-deps
}
