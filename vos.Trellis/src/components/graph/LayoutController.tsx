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

function killSupervisor(reference: React.MutableRefObject<FA2Supervisor | null>) {
  reference.current?.kill();
  reference.current = null;
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
 * Every graph runs the force layout in a worker, so panning and clicking stay responsive while it runs.
 *
 * Clustering by predicate needs no layout of its own on the main thread: the active predicates' members are
 * left free, every other node is pinned with the `fixed` attribute, and every other edge gets zero weight so it
 * pulls on nothing. The layout reads both on the main thread as it starts.
 *
 * No layout runs while the graph is empty, which hiding every type reaches. The worker's loop is paced only by
 * how long a step takes, so with nothing to place it trades messages with the main thread fast enough to stop
 * the page drawing.
 */
export function LayoutController() {
  const sigma = useSigma();
  const isPopulated = useGraphIsPopulated(sigma.getGraph());
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);

  const activePredicateKey = [...activePredicateIds].sort().join(',');
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const layoutSettings = useUiStore((s) => s.layoutSettings);

  const supervisorReference = useRef<FA2Supervisor | null>(null);

  const layoutKey = JSON.stringify(layoutSettings);

  useEffect(() => {
    const graph = sigma.getGraph();

    killSupervisor(supervisorReference);
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
    supervisorReference.current = supervisor;

    return () => {
      killSupervisor(supervisorReference);
      clearFixedFlags(graph);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sigma, isPopulated, activePredicateKey, layoutKey, isSpreadActive]);

  useEffect(() => {
    const supervisor = supervisorReference.current;
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
