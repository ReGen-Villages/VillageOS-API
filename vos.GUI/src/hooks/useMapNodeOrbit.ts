import { useEffect, useRef } from 'react';
import type Sigma from 'sigma';
import { useUiStore } from '../stores/uiStore';
import { computeOrbitPosition, ORBIT_RADIUS_FANOUT } from '../utils/nodeVisibility';

interface MapBinding {
  updateGraphCoordinates: (graph: import('graphology').default) => void;
}

/**
 * Manages orbit positioning for non-geo nodes around selected nodes on the map,
 * and fan-out of overlapping nodes on click.
 */
export function useMapNodeOrbit(
  sigma: Sigma,
  mapEnabled: boolean,
  bindingRef: React.RefObject<MapBinding | null>,
) {
  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const pendingFanOut = useUiStore((s) => s.pendingFanOut);
  const orbitingNodesRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    orbitingNodesRef.current.clear();
  }, [selectedNodeId]);

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
    const prev = orbitingNodesRef.current;
    allNodes.forEach((nodeId, i) => {
      if (!graph.hasNode(nodeId)) return;
      const pos = computeOrbitPosition(cLat, cLng, i, allNodes.length, ORBIT_RADIUS_FANOUT);
      graph.setNodeAttribute(nodeId, '_orbitLat', pos.lat);
      graph.setNodeAttribute(nodeId, '_orbitLng', pos.lng);
      prev.add(nodeId);
    });

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        binding.updateGraphCoordinates(graph);
      });
    });

    useUiStore.getState().setPendingFanOut(null);
  }, [pendingFanOut, mapEnabled, sigma, bindingRef]);
}
