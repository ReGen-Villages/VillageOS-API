import { useEffect, useMemo } from 'react';
import { useSetSettings, useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import type { SearchOptions } from '../../utils/searchFilter';
import {
  buildLabelMatcher,
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
 * Edge visibility:
 * - Default: ALL edges hidden
 * - Hover: edges touching hovered node brightened
 * - Selection: edges touching selected node visible
 * - Search: edges where both endpoints match are visible
 * - Predicates: edges matching active predicate filters visible
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function NodeReducer({ searchQuery, searchOptions }: Props) {
  const setSettings = useSetSettings();
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
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

      if (edgeTouchesNode(graph, edge, hovered)) return brightenEdge(data);
      if (selected && edgeTouchesNode(graph, edge, selected)) return data;

      if (matchedNodes.size > 0) {
        const src = graph.source(edge);
        const tgt = graph.target(edge);
        if (matchedNodes.has(src) && matchedNodes.has(tgt)) return data;
      }

      if (activePredicateIds.size > 0) {
        const predicateId = data.predicateId as string;
        if (activePredicateIds.has(predicateId)) return data;
      }

      return { ...data, hidden: true };
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQuery, labelMatcher, activePredicateIds, clusterMap, selectedNodeId, setSettings, sigma]);

  return null;
}
