import React, { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';
import ForceSupervisor from 'graphology-layout-force/worker';
import FA2Supervisor from 'graphology-layout-forceatlas2/worker';
import { useUiStore } from '../../stores/uiStore';

/**
 * Node count above which we switch from graphology-layout-force (O(N²) on
 * main thread) to ForceAtlas2 (Barnes-Hut O(N log N) in a real Web Worker).
 */
const FA2_THRESHOLD = 2000;

/** Kill the current supervisor and clear the ref. */
function killSupervisor(ref: React.MutableRefObject<ForceSupervisor | FA2Supervisor | null>) {
  ref.current?.kill();
  ref.current = null;
}

/**
 * Manages the force-directed layout lifecycle.
 *
 * For **small graphs** (< FA2_THRESHOLD nodes): uses graphology-layout-force
 * which supports `shouldSkipNode`, `shouldSkipEdge`, and `isNodeFixed`
 * callbacks needed for predicate clustering and map geo-pinning.
 *
 * For **large graphs** (>= FA2_THRESHOLD nodes): uses ForceAtlas2 with
 * Barnes-Hut approximation running in a real Web Worker, so the main thread
 * stays responsive.  FA2 uses a `fixed` node attribute for geo-pinning
 * instead of a callback.
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function LayoutController() {
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);

  // Stable serialization for React dependency tracking
  const activePredicateKey = [...activePredicateIds].sort().join(',');
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const layoutSettings = useUiStore((s) => s.layoutSettings);

  // Either supervisor type — both have start()/stop()/kill()
  const supervisorRef = useRef<ForceSupervisor | FA2Supervisor | null>(null);

  // Stable key for layout settings to avoid unnecessary re-creates
  const layoutKey = JSON.stringify(layoutSettings);

  // Create / recreate the supervisor when clustering, spread, or settings change
  useEffect(() => {
    const graph = sigma.getGraph();

    // Kill any previous supervisor
    killSupervisor(supervisorRef);

    const isClustering = activePredicateIds.size > 0;
    const isLargeGraph = graph.order >= FA2_THRESHOLD;

    // ── ForceAtlas2 path (large graphs) ─────────────────────────────────
    // FA2 runs in a real Web Worker with Barnes-Hut O(N log N) repulsion,
    // keeping the main thread responsive for 10K+ node graphs.
    // FA2 doesn't support shouldSkipNode/Edge callbacks, but that's fine:
    // predicate clustering is handled visually by NodeReducer, and FA2's
    // natural force-directed behavior already groups connected nodes.
    if (isLargeGraph) {
      // FA2 uses a `fixed` node attribute — only set when map is on
      if (mapEnabled) {
        graph.forEachNode((node, attrs) => {
          if (attrs.hasGeometry) graph.setNodeAttribute(node, 'fixed', true);
        });
      }

      const baseGravity = layoutSettings.gravity * 10000;
      const gravity = isSpreadActive ? baseGravity * 0.1 : baseGravity;

      const supervisor = new FA2Supervisor(graph, {
        settings: {
          gravity,
          scalingRatio: layoutSettings.repulsion * 100,
          barnesHutOptimize: true,
          barnesHutTheta: 0.5,
          slowDown: 5,
          strongGravityMode: false,
        },
      });

      if (!isLayoutFrozen && !mapEnabled) {
        supervisor.start();
      }
      supervisorRef.current = supervisor;

      return () => {
        killSupervisor(supervisorRef);
      };
    }

    // ── Simple force layout path (small graphs or clustering) ──────────
    // When clustering, build a set of nodes that participate in at least one
    // active-predicate edge. Nodes outside this set are excluded from the
    // simulation entirely so they don't repel the visible cluster.
    let clusterNodeIds: Set<string> | null = null;
    if (isClustering) {
      clusterNodeIds = new Set<string>();
      graph.forEachEdge((_edge, attrs, source, target) => {
        if (activePredicateIds.has(attrs.predicateId as string)) {
          clusterNodeIds!.add(source);
          clusterNodeIds!.add(target);
        }
      });
    }

    // In spread mode: boost repulsion 5x, reduce gravity 10x
    const baseRepulsion = isClustering ? layoutSettings.clusterRepulsion : layoutSettings.repulsion;
    const repulsion = isSpreadActive ? baseRepulsion * 5 : baseRepulsion;
    const gravity = isSpreadActive ? layoutSettings.gravity * 0.1 : layoutSettings.gravity;

    const supervisor = new ForceSupervisor(graph, {
      isNodeFixed: (_key: string, attrs: Record<string, unknown>) => {
        // Only pin geo nodes when the map is active (MapLibre controls positions)
        const isMapOn = useUiStore.getState().mapEnabled;
        if (isMapOn && attrs.hasGeometry) return true;
        return false;
      },

      // When clustering: skip nodes not connected by any active predicate edge
      shouldSkipNode: clusterNodeIds
        ? (key: string) => !clusterNodeIds!.has(key)
        : undefined,

      // When clustering: skip edges that don't match any active predicate,
      // so only predicate-matching edges create attraction forces
      shouldSkipEdge: isClustering
        ? (_edge: string, attrs: Record<string, unknown>) =>
            !activePredicateIds.has(attrs.predicateId as string)
        : undefined,

      settings: {
        attraction: layoutSettings.attraction,
        repulsion,
        gravity,
        inertia: layoutSettings.inertia,
        maxMove: layoutSettings.maxMove,
      },
    });

    // Only start if layout is not frozen and map is not enabled
    if (!isLayoutFrozen && !mapEnabled) {
      supervisor.start();
    }
    supervisorRef.current = supervisor;

    return () => {
      killSupervisor(supervisorRef);
    };
    // Note: isLayoutFrozen is intentionally read but not in the dep array —
    // we don't want to recreate the supervisor when freeze toggles.
    // The second useEffect handles start/stop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sigma, activePredicateKey, mapEnabled, layoutKey, isSpreadActive]);

  // Stop / start the supervisor when freeze or map toggle changes.
  // Also toggle expensive Sigma settings for large graphs.
  useEffect(() => {
    const supervisor = supervisorRef.current;
    if (!supervisor) return;

    const graph = sigma.getGraph();
    const isLarge = graph.order >= FA2_THRESHOLD;

    if (isLayoutFrozen || mapEnabled) {
      supervisor.stop();
      // Force a final refresh after stopping to ensure no pending frames
      // are left that could race with MapLibre coordinate updates
      requestAnimationFrame(() => {
        sigma.refresh();
      });
    } else {
      supervisor.start();
    }

    // For large graphs, disable costly per-frame work while layout is running
    if (isLarge) {
      const layoutRunning = !isLayoutFrozen && !mapEnabled;
      sigma.setSetting('enableEdgeEvents', !layoutRunning);
      sigma.setSetting('renderEdgeLabels', !layoutRunning);
    }
  }, [isLayoutFrozen, mapEnabled, sigma]);

  return null;
}
