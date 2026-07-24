import React, { useEffect, useRef, useState } from 'react';
import { useSigma } from '@react-sigma/core';
import type Graph from 'graphology';
import FA2Supervisor from 'graphology-layout-forceatlas2/worker';
import { useUiStore } from '../../stores/uiStore';
import { resolveFA2Settings } from '../../utils/fa2Settings';
import {
  collectClusterNodeIds,
  applyClusterFixedFlags,
  clearFixedFlags,
  makeActivePredicateWeightGetter,
} from '../../utils/clusterLayout';

function killSupervisor(ref: React.MutableRefObject<FA2Supervisor | null>) {
  ref.current?.kill();
  ref.current = null;
}

/** Re-renders only on the empty ↔ populated transition, not once per node. */
function useGraphIsPopulated(graph: Graph): boolean {
  const [isPopulated, setIsPopulated] = useState(() => graph.order > 0);

  useEffect(() => {
    const sync = () => setIsPopulated(graph.order > 0);
    // GraphDataLoader is an earlier sibling, so it can fill the graph between
    // this component's render and this subscription.
    sync();

    graph.on('nodeAdded', sync);
    graph.on('nodeDropped', sync);
    graph.on('cleared', sync);

    return () => {
      graph.off('nodeAdded', sync);
      graph.off('nodeDropped', sync);
      graph.off('cleared', sync);
    };
  }, [graph]);

  return isPopulated;
}

/**
 * Force-directed layout lifecycle. Every graph runs the worker-based
 * ForceAtlas2 (Barnes-Hut O(N log N), off the main thread) so panning and
 * clicking stay responsive while the layout runs.
 *
 * Predicate clustering has no dedicated engine: instead of a main-thread
 * supervisor with shouldSkipNode/shouldSkipEdge callbacks, the active-predicate
 * members are freed and every other node is pinned via the `fixed` attribute,
 * and non-active edges are given zero weight so they exert no attraction. Both
 * are read by FA2 on the main thread at matrix-build time. Child of
 * <SigmaContainer>.
 *
 * No supervisor runs while the graph is empty. FA2's worker loop is paced only
 * by how long an iteration takes, so with nothing to lay out it ping-pongs
 * messages with the main thread as fast as the two can post — enough to starve
 * rendering. An empty graph is reachable from the UI: hide every type.
 */
export function LayoutController() {
  const sigma = useSigma();
  const isPopulated = useGraphIsPopulated(sigma.getGraph());
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);

  const activePredicateKey = [...activePredicateIds].sort().join(',');
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const layoutSettings = useUiStore((s) => s.layoutSettings);

  const supervisorRef = useRef<FA2Supervisor | null>(null);

  const layoutKey = JSON.stringify(layoutSettings);

  useEffect(() => {
    const graph = sigma.getGraph();

    killSupervisor(supervisorRef);
    if (!isPopulated) return;

    const isClustering = activePredicateIds.size > 0;

    if (isClustering) {
      const clusterNodeIds = collectClusterNodeIds(graph, activePredicateIds);
      applyClusterFixedFlags(graph, clusterNodeIds);
    } else {
      clearFixedFlags(graph);
    }

    const supervisor = new FA2Supervisor(graph, {
      settings: resolveFA2Settings(layoutSettings, isSpreadActive, isClustering),
      getEdgeWeight: isClustering
        ? makeActivePredicateWeightGetter(activePredicateIds)
        : undefined,
    });

    if (!isLayoutFrozen) {
      supervisor.start();
    }
    supervisorRef.current = supervisor;

    return () => {
      killSupervisor(supervisorRef);
      clearFixedFlags(graph);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sigma, isPopulated, activePredicateKey, layoutKey, isSpreadActive]);

  // Stop / start the supervisor when freeze toggle changes.
  useEffect(() => {
    const supervisor = supervisorRef.current;
    if (!supervisor) return;

    if (isLayoutFrozen) {
      supervisor.stop();
      requestAnimationFrame(() => {
        sigma.refresh();
      });
    } else {
      supervisor.start();
    }

    const layoutRunning = !isLayoutFrozen;
    sigma.setSetting('enableEdgeEvents', !layoutRunning);
    sigma.setSetting('renderEdgeLabels', !layoutRunning);
  }, [isLayoutFrozen, sigma]);

  return null;
}
