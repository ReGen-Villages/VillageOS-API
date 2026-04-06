import { useEffect } from 'react';
import { useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import { useModelStore } from '../../stores/modelStore';
import { computePredicateStatsFromModel, computeClusters } from '../../utils/predicateCluster';

/**
 * Renderless component that computes predicate stats and cluster maps.
 * Runs inside <SigmaContainer> to access the graphology graph.
 *
 * Responsibilities:
 * 1. When graph data changes → recompute predicate stats
 * 2. When activePredicateIds changes → compute clusters via BFS (union of edges)
 */
export function ClusterComputer() {
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);

  // Stable serialization of the set for use as a React dependency
  const activePredicateKey = [...activePredicateIds].sort().join(',');

  // Recompute predicate stats from full model data (not filtered graph)
  // so predicates like consumes/produces always appear in the radial menu
  // even when map-mode surface reduction filters out their endpoints.
  useEffect(() => {
    if (things.length === 0) return;
    const state = useUiStore.getState();
    const stats = computePredicateStatsFromModel(things, relationships, state.predicateColors);
    state.setPredicateStats(stats);
  }, [things, relationships]);

  // Keep clusters in sync with graph structure changes
  useEffect(() => {
    const graph = sigma.getGraph();

    const handleUpdate = () => {
      if (graph.order === 0) return;
      // Recompute clusters when graph edges change (e.g. after remount
      // or SignalR updates). Critical after remount: the Zustand clusterMap
      // is stale but the timer in the predicate effect already fired on
      // an empty graph.
      const currentIds = useUiStore.getState().activePredicateIds;
      if (currentIds.size > 0) {
        useUiStore.getState().setClusterMap(computeClusters(graph, currentIds));
      }
    };

    handleUpdate();

    graph.on('edgeAdded', handleUpdate);
    graph.on('edgeDropped', handleUpdate);
    graph.on('cleared', handleUpdate);

    return () => {
      graph.off('edgeAdded', handleUpdate);
      graph.off('edgeDropped', handleUpdate);
      graph.off('cleared', handleUpdate);
    };
  }, [sigma]);

  // Recompute clusters when activePredicateIds changes
  useEffect(() => {
    const graph = sigma.getGraph();

    if (activePredicateIds.size === 0) {
      useUiStore.getState().setClusterMap(null);
      return;
    }

    // Small delay — allow graph data to settle after SignalR updates
    const timer = setTimeout(() => {
      if (graph.order === 0) {
        // Graph not loaded yet — clear stale cluster data so NodeReducer
        // falls through to default reducers instead of using outdated maps
        useUiStore.getState().setClusterMap(null);
        return;
      }
      const clusterMap = computeClusters(graph, activePredicateIds);
      useUiStore.getState().setClusterMap(clusterMap);
    }, 50);

    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sigma, activePredicateKey]);

  return null;
}
