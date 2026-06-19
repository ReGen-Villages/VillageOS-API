import React, { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';
import ForceSupervisor from 'graphology-layout-force/worker';
import FA2Supervisor from 'graphology-layout-forceatlas2/worker';
import { useUiStore } from '../../stores/uiStore';
import { resolveFA2Settings } from '../../utils/fa2Settings';

/**
 * Above this node count, switch from graphology-layout-force (O(N²), main
 * thread) to ForceAtlas2 (Barnes-Hut O(N log N), Web Worker).
 */
const FA2_THRESHOLD = 2000;

function killSupervisor(ref: React.MutableRefObject<ForceSupervisor | FA2Supervisor | null>) {
  ref.current?.kill();
  ref.current = null;
}

/**
 * Force-directed layout lifecycle. Small graphs use graphology-layout-force
 * (needed for the shouldSkipNode/Edge callbacks that drive predicate
 * clustering); large graphs use worker-based ForceAtlas2. Child of <SigmaContainer>.
 */
export function LayoutController() {
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);

  const activePredicateKey = [...activePredicateIds].sort().join(',');
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const layoutSettings = useUiStore((s) => s.layoutSettings);

  const supervisorRef = useRef<ForceSupervisor | FA2Supervisor | null>(null);

  const layoutKey = JSON.stringify(layoutSettings);

  useEffect(() => {
    const graph = sigma.getGraph();

    killSupervisor(supervisorRef);

    const isClustering = activePredicateIds.size > 0;
    const isLargeGraph = graph.order >= FA2_THRESHOLD;

    if (isLargeGraph) {
      const supervisor = new FA2Supervisor(graph, {
        settings: resolveFA2Settings(layoutSettings, isSpreadActive),
      });

      if (!isLayoutFrozen) {
        supervisor.start();
      }
      supervisorRef.current = supervisor;

      return () => {
        killSupervisor(supervisorRef);
      };
    }

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

    // Spread mode: 5x repulsion, 0.1x gravity.
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
