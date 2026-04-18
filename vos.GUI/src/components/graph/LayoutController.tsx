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
 * callbacks needed for predicate clustering.
 *
 * For **large graphs** (>= FA2_THRESHOLD nodes): uses ForceAtlas2 with
 * Barnes-Hut approximation running in a real Web Worker, so the main thread
 * stays responsive.
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

    if (isLargeGraph) {
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

      if (!isLayoutFrozen) {
        supervisor.start();
      }
      supervisorRef.current = supervisor;

      return () => {
        killSupervisor(supervisorRef);
      };
    }

    // ── Simple force layout path (small graphs or clustering) ──────────
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
      shouldSkipNode: clusterNodeIds
        ? (key: string) => !clusterNodeIds!.has(key)
        : undefined,

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

    if (!isLayoutFrozen) {
      supervisor.start();
    }
    supervisorRef.current = supervisor;

    return () => {
      killSupervisor(supervisorRef);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sigma, activePredicateKey, layoutKey, isSpreadActive]);

  // Stop / start the supervisor when freeze toggle changes.
  useEffect(() => {
    const supervisor = supervisorRef.current;
    if (!supervisor) return;

    const graph = sigma.getGraph();
    const isLarge = graph.order >= FA2_THRESHOLD;

    if (isLayoutFrozen) {
      supervisor.stop();
      requestAnimationFrame(() => {
        sigma.refresh();
      });
    } else {
      supervisor.start();
    }

    if (isLarge) {
      const layoutRunning = !isLayoutFrozen;
      sigma.setSetting('enableEdgeEvents', !layoutRunning);
      sigma.setSetting('renderEdgeLabels', !layoutRunning);
    }
  }, [isLayoutFrozen, sigma]);

  return null;
}
