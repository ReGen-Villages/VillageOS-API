import { useEffect, useMemo } from 'react';
import { useSetSettings, useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import type { SearchOptions } from '../../utils/searchFilter';
import {
  buildLabelMatcher,
  decideEdgeDisplay,
  edgeTouchesNode,
} from '../../utils/nodeVisibility';
import {
  applySelectionHighlight,
  brightenEdge,
  computeClusterNodeStyle,
  applyNodeFlash,
  applyEdgeFlash,
} from '../../utils/reducerHelpers';

interface Props {
  searchQuery: string;
  searchOptions: SearchOptions;
}

/**
 * Installs Sigma nodeReducer/edgeReducer for visual filtering.
 *
 * Edge visibility (Bug #5343):
 * - Default: ALL edges visible
 * - Hover: edges touching hovered node brightened
 * - Selection: edges touching selected node visible (others fall under default)
 * - Search active: only edges connecting matched nodes visible (others hidden)
 * - Predicate filter active: only edges with matching predicate visible (others hidden)
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function NodeReducer({ searchQuery, searchOptions }: Props) {
  const setSettings = useSetSettings();
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const hiddenPredicateIds = useUiStore((s) => s.hiddenPredicateIds);
  const clusterMap = useUiStore((s) => s.clusterMap);
  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const flashingNodeIds = useUiStore((s) => s.flashingNodeIds);
  const flashingEdgeIds = useUiStore((s) => s.flashingEdgeIds);

  const labelMatcher = useMemo(
    () => (searchQuery ? buildLabelMatcher(searchQuery, searchOptions) : null),
    [searchQuery, searchOptions],
  );

  useEffect(() => {
    if (flashingNodeIds.size > 0 || flashingEdgeIds.size > 0) sigma.refresh();
  }, [flashingNodeIds, flashingEdgeIds, sigma]);

  useEffect(() => {
    let prevHovered: string | null = useUiStore.getState().hoveredNodeId;
    return useUiStore.subscribe((state) => {
      if (state.hoveredNodeId !== prevHovered) {
        prevHovered = state.hoveredNodeId;
        sigma.refresh();
      }
    });
  }, [sigma]);

  const withNodeFlash = (
    base: (node: string, data: Record<string, unknown>) => Record<string, unknown>,
  ) => (node: string, data: Record<string, unknown>) => {
    let result = base(node, data);
    if (result.hidden) return result;
    const state = useUiStore.getState();
    if (state.flashingNodeIds.size > 0 && state.flashingNodeIds.has(node))
      result = applyNodeFlash(result, state.flashSettings);
    if (node !== state.hoveredNodeId && node !== state.selectedNodeId) {
      result = { ...result, label: '' };
    }
    return result;
  };

  const withEdgeFlash = (
    base: (edge: string, data: Record<string, unknown>) => Record<string, unknown>,
  ) => (edge: string, data: Record<string, unknown>) => {
    const result = base(edge, data);
    if (result.hidden) return result;
    const state = useUiStore.getState();
    if (state.flashingEdgeIds.size > 0 && state.flashingEdgeIds.has(edge))
      return applyEdgeFlash(result, state.flashSettings);
    return result;
  };

  useEffect(() => {
    const graph = sigma.getGraph();

    const matchedNodes = new Set<string>();
    if (searchQuery && labelMatcher) {
      graph.forEachNode((node, attrs) => {
        const label = (attrs.label as string) || '';
        if (labelMatcher(label)) matchedNodes.add(node);
      });
    }

    const edgeReducer = withEdgeFlash((edge: string, data: Record<string, unknown>) => {
      const currentState = useUiStore.getState();
      const hovered = currentState.hoveredNodeId;
      const selected = currentState.selectedNodeId;

      // Bug #5365 — predicate filter (PredicateFilterPanel) is an absolute
      // hide. If the edge's predicate is in the user's hidden set, the edge
      // never renders, regardless of hover / selection / search / clustering.
      // Mirrors the type filter's behavior so checkbox state is the single
      // source of truth.
      if (hiddenPredicateIds.has(data.predicateId as string)) {
        return { ...data, hidden: true };
      }

      const src = graph.source(edge);
      const tgt = graph.target(edge);

      const decision = decideEdgeDisplay({
        endpointMatchesHover: edgeTouchesNode(graph, edge, hovered),
        endpointMatchesSelection: edgeTouchesNode(graph, edge, selected),
        bothEndpointsInSearch: matchedNodes.size > 0
          ? matchedNodes.has(src) && matchedNodes.has(tgt)
          : undefined,
        predicateInActiveFilter: activePredicateIds.size > 0
          ? activePredicateIds.has(data.predicateId as string)
          : undefined,
      });

      switch (decision) {
        case 'brighten': return brightenEdge(data);
        case 'show': return data;
        case 'hide': return { ...data, hidden: true };
      }
    });

    // ── Priority 1: Search query ────────────────────────────────────────
    if (searchQuery && labelMatcher) {
      setSettings({
        nodeReducer: withNodeFlash((node: string, data: Record<string, unknown>) => {
          const selId = useUiStore.getState().selectedNodeId;
          if (matchedNodes.has(node)) return applySelectionHighlight(node, data, selId);
          if (data.isLogical) return { ...data, hidden: true };
          return applySelectionHighlight(node, {
            ...data, color: '#27272a', label: '', zIndex: 0,
          }, selId);
        }),
        edgeReducer,
      });
      return;
    }

    // ── Priority 2: Clustering active ───────────────────────────────────
    if (activePredicateIds.size > 0 && clusterMap) {
      setSettings({
        nodeReducer: withNodeFlash((node: string, data: Record<string, unknown>) => {
          const state = useUiStore.getState();
          const { collapsedClusters, expandedNodes } = state;
          const selId = state.selectedNodeId;
          return computeClusterNodeStyle(
            node, data, clusterMap, collapsedClusters, expandedNodes, selId, false,
          );
        }),
        edgeReducer,
      });
      return;
    }

    // ── Priority 3: Default — all nodes visible with selection highlight
    setSettings({
      nodeReducer: withNodeFlash((node: string, data: Record<string, unknown>) => {
        const selId = useUiStore.getState().selectedNodeId;
        return applySelectionHighlight(node, data, selId);
      }),
      edgeReducer,
    });
  }, [searchQuery, labelMatcher, activePredicateIds, hiddenPredicateIds, clusterMap, selectedNodeId, setSettings, sigma]);

  return null;
}
