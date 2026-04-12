import { useEffect, useMemo, useRef } from 'react';
import { useSigma } from '@react-sigma/core';
import { LngLatBounds, type Map as MaplibreMap } from 'maplibre-gl';
import bindMaplibreLayer from '@sigma/layer-maplibre';
import type { VosThing } from '../../types/vos';
import { useUiStore } from '../../stores/uiStore';
import { isGeoNode } from '../../utils/nodeVisibility';
import { setMapInstance } from '../../lib/mapInstance';
import { enlargeDegenerateBounds } from '../../lib/bboxFloor';
import { useMapFootprintLayers, FOOTPRINT_FILL_LAYER, FOOTPRINT_LINE_LAYER, FOOTPRINT_EXTRUSION_LAYER } from '../../hooks/useMapFootprintLayers';
import { useMapNodeOrbit } from '../../hooks/useMapNodeOrbit';

const CARTO_DARK_STYLE =
  'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json';


interface Props {
  things: VosThing[];
}

interface MapBinding {
  clean: () => void;
  map: MaplibreMap;
  updateGraphCoordinates: (graph: import('graphology').default) => void;
}

/**
 * Binds a MapLibre GL dark-matter map as a background layer behind Sigma's
 * WebGL node/edge rendering.  Activated by the `mapEnabled` flag in uiStore.
 *
 * Must be rendered as a child of <SigmaContainer>.
 */

export function MaplibreLayer({ things }: Props) {
  const sigma = useSigma();
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const threeDEnabled = useUiStore((s) => s.threeDEnabled);
  const bindingRef = useRef<MapBinding | null>(null);

  // Structural fingerprint — only changes when the set of thing IDs changes,
  // not when property values are updated.  Used by effects that should NOT
  // re-run on property-only changes (coordinate sync, footprint rebuild).
  const thingIds = useMemo(() => things.map((t) => t.Id).join(','), [things]);

  /** Target pitch injected into fitBounds calls (see pitch patch below). */
  const pitchRef = useRef(0);
  /** Suppresses moveend events to prevent Map→Sigma feedback during pitch changes. */
  const suppressMoveEndRef = useRef(false);
  /** While true, fitBounds calls enlarge degenerate bboxes to MIN_BBOX_SPAN_DEG.
   *  Cleared once the initial tile-load window elapses so user-driven zoom/pan
   *  is not clamped by the floor (Bug #5166). */
  const floorDegenerateBoundsRef = useRef(true);

  // ── Bind / unbind the map layer ────────────────────────────────────────
  useEffect(() => {
    if (!mapEnabled) {
      // Tear down existing binding
      if (bindingRef.current) {
        bindingRef.current.clean();
        bindingRef.current = null;
        setMapInstance(null);

        // Belt-and-suspenders: remove any leftover maplibre DOM elements
        // that sigma.killLayer may have missed
        const container = sigma.getContainer();
        container.querySelectorAll('.maplibregl-map, .mapboxgl-map, [class*="maplibre"]').forEach((el) => {
          el.remove();
        });

        // Reset node positions from Mercator back to a circular layout.
        // Without this, geo nodes remain at huge Mercator coordinates that
        // the force layout can't handle (they're pinned by isNodeFixed),
        // causing the graph to be invisible or wildly off-screen.
        const graph = sigma.getGraph();
        const nodeCount = graph.order;
        const angleStep = (2 * Math.PI) / Math.max(nodeCount, 1);
        const radius = 100 + nodeCount * 5;
        let idx = 0;
        graph.forEachNode((nodeId) => {
          graph.setNodeAttribute(nodeId, 'x', radius * Math.cos(angleStep * idx));
          graph.setNodeAttribute(nodeId, 'y', radius * Math.sin(angleStep * idx));
          idx++;
        });

        // After removing MapLibre's WebGL context the camera is still at
        // the Mercator-sync ratio (very zoomed in). Reset it immediately
        // so nodes don't render as giant discs, then refresh.
        sigma.getCamera().setState({ x: 0.5, y: 0.5, angle: 0, ratio: 1 });
        sigma.refresh();
      }
      return;
    }

    // Already bound — skip
    if (bindingRef.current) {
      return;
    }

    // Compute the centroid of all geo nodes so non-geo nodes can be placed
    // nearby (hidden but keeping the camera bounding box tight).
    let totalLat = 0, totalLng = 0, geoCount = 0;
    const graph = sigma.getGraph();
    graph.forEachNode((_id, attrs) => {
      if (isGeoNode(attrs)) {
        totalLat += attrs.lat as number;
        totalLng += attrs.lng as number;
        geoCount++;
      }
    });
    const geoCentroid = geoCount > 0
      ? { lat: totalLat / geoCount, lng: totalLng / geoCount }
      : { lat: 0, lng: 0 };

    const binding = bindMaplibreLayer(sigma, {
      mapOptions: {
        style: CARTO_DARK_STYLE,
        attributionControl: false,
        preserveDrawingBuffer: true,
        // Start loading tiles at the correct location immediately.
        // Without this, the map defaults to [0,0] zoom 1 (dark world view)
        // and relies on the sigma→map sync loop to reposition — but that
        // sync can be delayed by the moveend suppression window, leaving
        // the user staring at Carto's dark-matter world view ("solid grey").
        center: [geoCentroid.lng, geoCentroid.lat] as any,
        zoom: 14,
      } as any,
      getNodeLatLng: (attrs) => {
        if (isGeoNode(attrs)) {
          return { lat: attrs.lat, lng: attrs.lng };
        }
        // Selection-orbiting logical nodes get dynamic lat/lng
        if (typeof attrs._orbitLat === 'number' && typeof attrs._orbitLng === 'number') {
          return { lat: attrs._orbitLat, lng: attrs._orbitLng };
        }
        // Non-geo / logical nodes: place at geo centroid of all geo nodes.
        // They are hidden by the nodeReducer so they don't render,
        // and being near the geo nodes means the camera fits correctly
        // instead of zooming out to show the whole world.
        return geoCentroid;
      },
    });

    bindingRef.current = binding;
    // Expose the map instance to GraphToolbar so its zoom/fit buttons can
    // act on MapLibre directly in map mode (Bug #5166). Cleared in the cleanup
    // below when the layer is torn down.
    setMapInstance(binding.map);
    // Expose for debugging (dev only)
    if (import.meta.env.DEV) (window as any).__maplibreMap = binding.map;

    // @sigma/layer-maplibre hardcodes minPitch:0, maxPitch:0 which prevents
    // fill-extrusion layers from being visible. Unlock maxPitch and patch
    // fitBounds so the Sigma→Map sync loop preserves our target pitch without
    // causing a zoom-out feedback loop.
    //
    // Strategy: fitBounds always calculates zoom at pitch:0 (overhead) to keep
    // the same zoom level, but we inject the target pitch as a *visual-only*
    // transform by setting it immediately after fitBounds completes. To prevent
    // the moveend→syncSigmaWithMap feedback that would zoom out, we temporarily
    // suppress the map's moveend event during the pitch application.
    const map = binding.map;

    map.setMaxPitch(60);
    // Suppress 'moveend' during fitBounds and pitch changes to prevent the
    // Map→Sigma sync from creating a feedback loop when pitch > 0.
    // Intercept moveend listeners registered by @sigma/layer-maplibre.
    // MapLibre's internal event dispatch bypasses instance-level `fire` overrides,
    // so we wrap the handler at registration time instead.
    const origOn = map.on.bind(map);
    map.on = (type: any, layerOrFn?: any, fn?: any) => {
      if (type === 'moveend') {
        const handler = typeof layerOrFn === 'function' ? layerOrFn : fn;
        const wrapped = (...args: any[]) => {
          if (suppressMoveEndRef.current) return;
          handler(...args);
        };
        return typeof layerOrFn === 'function'
          ? origOn(type, wrapped)
          : origOn(type, layerOrFn, wrapped);
      }
      return typeof fn === 'undefined'
        ? origOn(type, layerOrFn)
        : origOn(type, layerOrFn, fn);
    };
    const origFitBounds = map.fitBounds.bind(map);
    map.fitBounds = (bounds: any, opts?: any, ...rest: any[]) => {
      // After the initial tile-load window, suppress the sigma→map sync loop's
      // fitBounds calls (Bug #5166). @sigma/layer-maplibre always passes
      // {duration: 0} from syncMapWithSigma; GraphToolbar's user-driven calls
      // pass a nonzero duration. Without this guard, every user pan/zoom
      // gets clamped back by the next afterRender sync.
      if (!floorDegenerateBoundsRef.current && opts?.duration === 0) return map;

      // When pitched, suppress moveend permanently to prevent syncSigmaWithMap
      // from reading the wider pitched viewport bounds.  fitBounds fires moveend
      // ASYNCHRONOUSLY, so we can't just bracket the call — the flag must stay
      // on until pitch returns to 0 (handled by the toggle effect).
      if (pitchRef.current > 0) suppressMoveEndRef.current = true;
      // Floor degenerate bboxes so 1-2 near-coincident nodes don't collapse
      // the camera to MapLibre's max zoom (Bug #5165). Applies to every
      // fitBounds call that makes it past the sync-loop suppression above —
      // initial bind, the user's "Fit to viewport" button, etc. fitBounds
      // accepts both LngLatBounds and array-of-corners forms, so we normalise
      // to LngLatBounds first.
      let boundsObj: LngLatBounds;
      if (bounds instanceof LngLatBounds) {
        boundsObj = bounds;
      } else if (Array.isArray(bounds) && bounds.length === 2) {
        boundsObj = new LngLatBounds(bounds[0] as [number, number], bounds[1] as [number, number]);
      } else {
        // Unknown shape — pass through unchanged.
        return origFitBounds(bounds, { ...opts, pitch: pitchRef.current }, ...rest);
      }
      const enlarged = enlargeDegenerateBounds(boundsObj);
      const result = origFitBounds(enlarged, { ...opts, pitch: pitchRef.current }, ...rest);
      return result;
    };

    // Suppress the moveend→Sigma feedback loop during initial tile loading.
    // With large graphs (8K+ nodes), the Sigma render → afterRender →
    // fitBounds → moveend → syncSigma cycle is so CPU-intensive that
    // MapLibre never gets a chance to fetch and render tiles. Suppressing
    // moveend for a few seconds breaks the loop so tiles can load.
    // The same window also enables the degenerate-bbox floor so the initial
    // auto-fit lands at a sensible zoom when only 1-2 nodes are visible;
    // after the window elapses the floor is lifted to let user zoom work.
    suppressMoveEndRef.current = true;
    floorDegenerateBoundsRef.current = true;
    const tileLoadTimeout = setTimeout(() => {
      if (pitchRef.current === 0) suppressMoveEndRef.current = false;
      floorDegenerateBoundsRef.current = false;
    }, 4000);

    // Log map state after load for debugging
    if (import.meta.env.DEV) {
      map.once('load', () => {
        console.log('[MaplibreLayer] map loaded — center:', map.getCenter(), 'zoom:', map.getZoom());
      });
    }

    // Position all nodes in Mercator coordinates with a small delay.
    // We need to wait for the force layout to fully stop (it runs in a worker).
    const initTimeout = setTimeout(() => {
      if (bindingRef.current) {
        binding.updateGraphCoordinates(sigma.getGraph());
        // Fit the camera after coordinates update
        sigma.getCamera().animatedReset({ duration: 400 });
      }
    }, 20);

    // Safari and some browsers can lose the MapLibre canvas during resize
    // if @sigma/layer-maplibre's own handler hasn't been registered yet
    // (it waits for map "load"). This ensures the map resizes regardless.
    const handleWindowResize = () => {
      const map = bindingRef.current?.map;
      if (map) {
        requestAnimationFrame(() => {
          try {
            map.resize();
            sigma.refresh();
          } catch {
            // Safe to ignore if map/sigma are being torn down
          }
        });
      }
    };
    window.addEventListener('resize', handleWindowResize);

    return () => {
      clearTimeout(initTimeout);
      clearTimeout(tileLoadTimeout);
      window.removeEventListener('resize', handleWindowResize);
      if (bindingRef.current) {
        bindingRef.current.clean();
        bindingRef.current = null;
        setMapInstance(null);
        // Post-cleanup refresh for Safari context recovery
        requestAnimationFrame(() => {
          try {
            sigma.refresh();
          } catch {
            // Safe to ignore during unmount
          }
        });
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mapEnabled, sigma]);

  // Footprint layers managed by extracted hook
  useMapFootprintLayers(bindingRef.current?.map ?? null, mapEnabled, things);

  // ── Re-sync graph coordinates when nodes are added/removed ────────────
  const loadingPhase = useUiStore((s) => s.loadingPhase);

  useEffect(() => {
    const binding = bindingRef.current;
    if (!mapEnabled || !binding) return;

    // Skip coordinate resync during phase 2 (non-geo things loading).
    // Phase 2 adds nodes without geometry — no map work needed.
    // The resync will run after phase 3 when geometries arrive.
    if (loadingPhase === 'remaining') return;

    // Defer coordinate update to ensure force layout has fully stopped.
    // Without this delay, the force layout's pending animation frames can
    // race with MapLibre's coordinate transformation, causing duplication.
    // Double-RAF ensures we're past both the stop() and refresh() frames.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        binding.updateGraphCoordinates(sigma.getGraph());
      });
    });
  }, [mapEnabled, thingIds, sigma, loadingPhase]); // thingIds only changes on structural updates

  // ── Toggle between flat footprints and 3D extrusions ─────────────────
  useEffect(() => {
    const map = bindingRef.current?.map;
    if (!mapEnabled || !map) return;

    // Hide/show Sigma's canvas layers. When pitched, Sigma's 2D canvases
    // (nodes, edges, labels) sit on top and obscure the 3D extrusions.
    // We hide them so the MapLibre 3D view is visible, keeping only the
    // mouse-interaction canvas for click handling.
    const container = sigma.getContainer();
    const sigmaCanvases = container.querySelectorAll<HTMLCanvasElement>(
      'canvas.sigma-edges, canvas.sigma-edgeLabels, canvas.sigma-nodes, canvas.sigma-labels, canvas.sigma-hovers',
    );

    if (threeDEnabled) {
      pitchRef.current = 50;
      sigmaCanvases.forEach((c) => { c.style.display = 'none'; });
      if (map.getLayer(FOOTPRINT_FILL_LAYER)) map.setLayoutProperty(FOOTPRINT_FILL_LAYER, 'visibility', 'none');
      if (map.getLayer(FOOTPRINT_LINE_LAYER)) map.setLayoutProperty(FOOTPRINT_LINE_LAYER, 'visibility', 'none');
      if (map.getLayer(FOOTPRINT_EXTRUSION_LAYER)) map.setLayoutProperty(FOOTPRINT_EXTRUSION_LAYER, 'visibility', 'visible');
      // Trigger a Sigma refresh so afterRender→fitBounds picks up the new pitch.
      sigma.refresh();
    } else {
      pitchRef.current = 0;
      // Re-enable Map→Sigma sync now that pitch is back to 0
      suppressMoveEndRef.current = false;
      sigmaCanvases.forEach((c) => { c.style.display = ''; });
      if (map.getLayer(FOOTPRINT_FILL_LAYER)) map.setLayoutProperty(FOOTPRINT_FILL_LAYER, 'visibility', 'visible');
      if (map.getLayer(FOOTPRINT_LINE_LAYER)) map.setLayoutProperty(FOOTPRINT_LINE_LAYER, 'visibility', 'visible');
      if (map.getLayer(FOOTPRINT_EXTRUSION_LAYER)) map.setLayoutProperty(FOOTPRINT_EXTRUSION_LAYER, 'visibility', 'none');
      // Trigger a Sigma refresh so afterRender→fitBounds picks up pitch=0.
      sigma.refresh();
    }
  }, [mapEnabled, threeDEnabled, sigma]);

  // Orbit + fan-out managed by extracted hook
  useMapNodeOrbit(sigma, mapEnabled, bindingRef);

  return null;
}
