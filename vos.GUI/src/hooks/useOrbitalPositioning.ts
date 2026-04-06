import { useEffect, useRef } from 'react';
import type Sigma from 'sigma';
import { useUiStore } from '../stores/uiStore';

/** Radial offset in degrees for orbiting logical nodes around a geo node. */
const ORBIT_RADIUS_DEG = 0.0015;

interface MapBinding {
  map: import('maplibre-gl').Map;
  updateGraphCoordinates: (graph: import('graphology').default) => void;
}

/**
 * Positions non-geo nodes in a circle around the selected node (or its geo centroid)
 * and handles fan-out of overlapping nodes on click.
 */
export function useOrbitalPositioning(
  mapEnabled: boolean,
  sigma: Sigma,
  selectedNodeId: string | null,
  bindingRef: React.RefObject<MapBinding | null>,
) {
  const orbitingNodesRef = useRef<Set<string>>(new Set());
  const pendingFanOut = useUiStore((s) => s.pendingFanOut);

  // ── Orbit non-geo neighbors around selected node ─────────────────────
  useEffect(() => {
    const binding = bindingRef.current;
    if (!mapEnabled || !binding) return;

    const graph = sigma.getGraph();
    const prev = orbitingNodesRef.current;

    // Clear previous orbit attributes
    for (const id of prev) {
      if (graph.hasNode(id)) {
        graph.removeNodeAttribute(id, '_orbitLat');
        graph.removeNodeAttribute(id, '_orbitLng');
      }
    }
    prev.clear();

    if (selectedNodeId && graph.hasNode(selectedNodeId)) {
      const selAttrs = graph.getNodeAttributes(selectedNodeId);

      let centerLat: number;
      let centerLng: number;
      const nonGeoNeighbors: string[] = [];

      if (selAttrs.hasGeometry && typeof selAttrs.lat === 'number' && typeof selAttrs.lng === 'number') {
        centerLat = selAttrs.lat as number;
        centerLng = selAttrs.lng as number;
        graph.forEachNeighbor(selectedNodeId, (n, attrs) => {
          if (!attrs.hasGeometry) nonGeoNeighbors.push(n);
        });
      } else {
        let totalLat = 0;
        let totalLng = 0;
        let geoCount = 0;
        graph.forEachNeighbor(selectedNodeId, (n, attrs) => {
          if (attrs.hasGeometry && typeof attrs.lat === 'number' && typeof attrs.lng === 'number') {
            totalLat += attrs.lat as number;
            totalLng += attrs.lng as number;
            geoCount++;
          } else {
            nonGeoNeighbors.push(n);
          }
        });
        if (geoCount === 0) {
          requestAnimationFrame(() => {
            requestAnimationFrame(() => { binding.updateGraphCoordinates(graph); });
          });
          return;
        }
        centerLat = totalLat / geoCount;
        centerLng = totalLng / geoCount;
        nonGeoNeighbors.unshift(selectedNodeId);
      }

      if (nonGeoNeighbors.length > 0) {
        const angleStep = (2 * Math.PI) / nonGeoNeighbors.length;
        nonGeoNeighbors.forEach((nodeId, i) => {
          const angle = angleStep * i - Math.PI / 2;
          graph.setNodeAttribute(nodeId, '_orbitLat', centerLat + ORBIT_RADIUS_DEG * Math.sin(angle));
          graph.setNodeAttribute(nodeId, '_orbitLng', centerLng + ORBIT_RADIUS_DEG * Math.cos(angle));
          prev.add(nodeId);
        });
      }
    }

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        binding.updateGraphCoordinates(graph);
      });
    });
  }, [mapEnabled, selectedNodeId, sigma]);

  // ── Fan-out overlapping nodes on click ──────────────────────────────
  useEffect(() => {
    const binding = bindingRef.current;
    if (!pendingFanOut || !mapEnabled || !binding) return;

    const graph = sigma.getGraph();
    const { centerId, nodeIds } = pendingFanOut;
    if (!graph.hasNode(centerId)) {
      useUiStore.getState().setPendingFanOut(null);
      return;
    }

    const centerAttrs = graph.getNodeAttributes(centerId);
    let cLat: number;
    let cLng: number;
    if (typeof centerAttrs._orbitLat === 'number' && typeof centerAttrs._orbitLng === 'number') {
      cLat = centerAttrs._orbitLat as number;
      cLng = centerAttrs._orbitLng as number;
    } else if (typeof centerAttrs.lat === 'number' && typeof centerAttrs.lng === 'number') {
      cLat = centerAttrs.lat as number;
      cLng = centerAttrs.lng as number;
    } else {
      useUiStore.getState().setPendingFanOut(null);
      return;
    }

    const allNodes = [centerId, ...nodeIds];
    const angleStep = (2 * Math.PI) / allNodes.length;
    const prev = orbitingNodesRef.current;
    allNodes.forEach((nodeId, i) => {
      if (!graph.hasNode(nodeId)) return;
      const angle = angleStep * i - Math.PI / 2;
      graph.setNodeAttribute(nodeId, '_orbitLat', cLat + ORBIT_RADIUS_DEG * Math.sin(angle));
      graph.setNodeAttribute(nodeId, '_orbitLng', cLng + ORBIT_RADIUS_DEG * Math.cos(angle));
      prev.add(nodeId);
    });

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        binding.updateGraphCoordinates(graph);
      });
    });

    useUiStore.getState().setPendingFanOut(null);
  }, [pendingFanOut, mapEnabled, sigma]);
}
