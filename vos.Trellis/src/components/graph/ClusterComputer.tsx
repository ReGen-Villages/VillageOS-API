import { useEffect } from 'react';
import { useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import { useModelStore } from '../../stores/modelStore';
import { computePredicateStatisticsFromModel, computeClusters } from '../../utils/predicateCluster';

export function ClusterComputer() {
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);

  const activePredicateKey = [...activePredicateIds].sort().join(',');

  // Counted from the whole model rather than the drawn graph, so a predicate still appears when the map view
  // hides its endpoints.
  useEffect(() => {
    if (things.length === 0) return;
    const state = useUiStore.getState();
    const statistics = computePredicateStatisticsFromModel(things, relationships, state.predicateColors);
    state.setPredicateStatistics(statistics);
  }, [things, relationships]);

  useEffect(() => {
    const graph = sigma.getGraph();

    const handleUpdate = () => {
      if (graph.order === 0) return;
      // After remount the clusterMap is stale but the predicate-effect timer
      // already fired on an empty graph, so recompute here on edge changes.
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

  useEffect(() => {
    const graph = sigma.getGraph();

    if (activePredicateIds.size === 0) {
      useUiStore.getState().setClusterMap(null);
      return;
    }

    // The delay lets the graph settle after a run of streamed updates.
    const timer = setTimeout(() => {
      if (graph.order === 0) {
        // Graph not loaded yet — clear stale data so NodeReducer falls
        // through to defaults instead of using outdated maps.
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
