import { useEffect, useMemo, useRef } from 'react';
import { useSetSettings, useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import type { SearchOptions } from '../../utils/searchFilter';
import {
  buildLabelMatcher,
  getFullNeighborSet,
  edgeTouchesNode,
  computeOrbitPosition,
  ORBIT_RADIUS_SELECTION,
} from '../../utils/nodeVisibility';
import {
  applySelectionHighlight,
  brightenEdge,
  applyGeoNodeOpacity,
  resolveNonGeoMapVisibility,
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
 * Edge visibility (all modes):
 * - Default: ALL edges hidden
 * - Hover: edges touching hovered node brightened
 * - Selection: edges touching selected node visible
 * - Search: edges where both endpoints match are visible
 * - Predicates: edges matching active predicate filters visible
 *
 * Node visibility:
 * - Map mode: non-geo nodes hidden unless revealed by selection or predicate
 * - Non-map mode: all nodes visible
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function NodeReducer({ searchQuery, searchOptions }: Props) {
  const setSettings = useSetSettings();
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const clusterMap = useUiStore((s) => s.clusterMap);
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const flashingNodeIds = useUiStore((s) => s.flashingNodeIds);
  const flashingEdgeIds = useUiStore((s) => s.flashingEdgeIds);

  // Build matcher once when query/options change — avoids rebuilding per node
  const labelMatcher = useMemo(
    () => (searchQuery ? buildLabelMatcher(searchQuery, searchOptions) : null),
    [searchQuery, searchOptions],
  );

  // Refresh Sigma rendering when flash state changes (without reinstalling reducers)
  useEffect(() => {
    if (flashingNodeIds.size > 0 || flashingEdgeIds.size > 0) sigma.refresh();
  }, [flashingNodeIds, flashingEdgeIds, sigma]);

  // Refresh when hover changes — the edge reducer reads hoveredNodeId from the
  // store at call time, so we just need Sigma to re-render.
  // Use a direct zustand subscription (not React) so the refresh fires
  // immediately on state change, bypassing React's batched render cycle.
  useEffect(() => {
    let prevHovered: string | null = useUiStore.getState().hoveredNodeId;
    return useUiStore.subscribe((state) => {
      if (state.hoveredNodeId !== prevHovered) {
        prevHovered = state.hoveredNodeId;
        sigma.refresh();
      }
    });
  }, [sigma]);

  /** Wrap a node reducer to apply flash pulse and label-on-hover on top. */
  const withNodeFlash = (
    base: (node: string, data: Record<string, unknown>) => Record<string, unknown>,
  ) => (node: string, data: Record<string, unknown>) => {
    let result = base(node, data);
    if (result.hidden) return result;
    const state = useUiStore.getState();
    if (state.flashingNodeIds.size > 0 && state.flashingNodeIds.has(node))
      result = applyNodeFlash(result, state.flashSettings);
    // Dense graphs: only show labels for hovered or selected nodes.
    // The hover renderer (drawDarkNodeHover) still draws the label on hover.
    if (node !== state.hoveredNodeId && node !== state.selectedNodeId) {
      result = { ...result, label: '' };
    }
    return result;
  };

  /** Wrap an edge reducer to apply flash pulse on top. */
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

  // Track the "visibility anchor" — the last geo-node selection (or null).
  // When a non-geo (logical) node is selected, visibility should NOT
  // recalculate — nodes that were visible stay visible.  Visibility only
  // changes when a physical (geo) node is selected or the selection is
  // cleared by clicking empty space.
  const fullNeighborsRef = useRef(new Set<string>());

  useEffect(() => {
    const graph = sigma.getGraph();

    // Only recompute visibility when selection is null or a geo node.
    // Non-geo selection keeps the previous visibility state.
    if (mapEnabled) {
      if (selectedNodeId === null) {
        fullNeighborsRef.current = new Set<string>();
      } else if (
        graph.hasNode(selectedNodeId) &&
        graph.getNodeAttribute(selectedNodeId, 'hasGeometry')
      ) {
        // Move PREVIOUS orbit nodes to the new parent's position BEFORE
        // computing the new neighbor set. Without this, old orbit nodes
        // at stale positions widen Sigma's auto-rescale bounding box,
        // causing the camera to jump on the 2nd+ selection.
        // Read selected node position once — used for both old-orbit relocation
        // and new-orbit pre-positioning.
        const sx = graph.getNodeAttribute(selectedNodeId, 'x') as number;
        const sy = graph.getNodeAttribute(selectedNodeId, 'y') as number;
        const sLat = graph.getNodeAttribute(selectedNodeId, 'lat') as number;
        const sLng = graph.getNodeAttribute(selectedNodeId, 'lng') as number;

        // Move PREVIOUS orbit nodes to the new parent's position BEFORE
        // computing the new neighbor set. Without this, old orbit nodes
        // at stale positions widen Sigma's auto-rescale bounding box,
        // causing the camera to jump on the 2nd+ selection.
        if (typeof sx === 'number' && typeof sy === 'number') {
          for (const oldId of fullNeighborsRef.current) {
            if (!graph.hasNode(oldId)) continue;
            if (graph.getNodeAttribute(oldId, 'hasGeometry')) continue;
            graph.mergeNodeAttributes(oldId, {
              x: sx, y: sy,
              _orbitLat: sLat, _orbitLng: sLng,
            });
          }
        }

        fullNeighborsRef.current = getFullNeighborSet(graph, selectedNodeId);

        // Pre-position newly revealed non-geo neighbors at the selected node's
        // x/y AND set _orbitLat/_orbitLng BEFORE setSettings triggers a Sigma
        // render. Without this, auto-rescale and the async updateGraphCoordinates
        // chain see stale positions (geoCentroid), causing camera jumps.
        if (typeof sx === 'number' && typeof sy === 'number') {
          // Collect non-geo neighbors first so we know the count for angular spacing
          const nonGeoIds: string[] = [];
          for (const nid of fullNeighborsRef.current) {
            if (!graph.hasNode(nid)) continue;
            if (graph.getNodeAttribute(nid, 'hasGeometry')) continue;
            nonGeoIds.push(nid);
          }
          nonGeoIds.forEach((nid, i) => {
            const pos = computeOrbitPosition(sLat, sLng, i, nonGeoIds.length, ORBIT_RADIUS_SELECTION);
            graph.mergeNodeAttributes(nid, {
              x: sx, y: sy,
              _orbitLat: pos.lat,
              _orbitLng: pos.lng,
            });
          });
        }
      }
      // else: non-geo node selected — keep ref unchanged
    }

    const fullNeighbors = fullNeighborsRef.current;

    // Pre-compute search-matched nodes for edge and node reducers
    const matchedNodes = new Set<string>();
    if (searchQuery && labelMatcher) {
      graph.forEachNode((node, attrs) => {
        const label = (attrs.label as string) || '';
        if (labelMatcher(label)) matchedNodes.add(node);
      });
    }

    // Pre-compute non-geo nodes that are endpoints of predicate-filtered edges.
    // In map mode these must be un-hidden (shown small/dim) so Sigma draws
    // the matching edges. Orbital positioning is handled by MaplibreLayer.
    const predicateRevealedNodes = new Set<string>();
    if (mapEnabled && activePredicateIds.size > 0) {
      graph.forEachEdge((_edge, attrs, source, target) => {
        if (!activePredicateIds.has(attrs.predicateId as string)) return;
        if (!graph.getNodeAttribute(source, 'hasGeometry')) predicateRevealedNodes.add(source);
        if (!graph.getNodeAttribute(target, 'hasGeometry')) predicateRevealedNodes.add(target);
      });
    }

    // ── Unified edge reducer ────────────────────────────────────────────
    // Edges are hidden by default. Visible only when triggered by hover,
    // selection, search match, or predicate filter.
    // Note: hoveredNodeId and selectedNodeId are read from the store at
    // call time (not from the closure) so the reducer stays correct even
    // when the effect hasn't re-run yet — avoids expensive reducer
    // reinstallation on every hover/selection change.
    const edgeReducer = withEdgeFlash((edge: string, data: Record<string, unknown>) => {
      const currentState = useUiStore.getState();
      const hovered = currentState.hoveredNodeId;
      const selected = currentState.selectedNodeId;

      // Hover brightening — always takes priority
      if (edgeTouchesNode(graph, edge, hovered)) return brightenEdge(data);

      // Selection: show edges touching selected node
      if (selected && edgeTouchesNode(graph, edge, selected)) return data;

      // Search: show edges where both endpoints match
      if (matchedNodes.size > 0) {
        const src = graph.source(edge);
        const tgt = graph.target(edge);
        if (matchedNodes.has(src) && matchedNodes.has(tgt)) return data;
      }

      // Predicate filter: show edges matching active predicates
      if (activePredicateIds.size > 0) {
        const predicateId = data.predicateId as string;
        if (activePredicateIds.has(predicateId)) return data;
      }

      // Default: hidden
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
          const isMapOn = state.mapEnabled;
          const selId = state.selectedNodeId;

          // In map mode, resolve non-geo visibility first
          if (isMapOn && !data.hasGeometry) {
            const result = resolveNonGeoMapVisibility(node, data, selId, fullNeighbors, predicateRevealedNodes)!;
            if (result.hidden && state.hoveredNodeId) {
              try {
                if (graph.hasNode(state.hoveredNodeId) && graph.areNeighbors(state.hoveredNodeId, node)) {
                  return { ...data, size: 2, zIndex: 1 };
                }
              } catch { /* node may not exist */ }
            }
            return result;
          }

          return computeClusterNodeStyle(
            node, data, clusterMap, collapsedClusters, expandedNodes, selId, isMapOn,
          );
        }),
        edgeReducer,
      });
      return;
    }

    // ── Priority 3: Default ─────────────────────────────────────────────
    if (mapEnabled) {
      setSettings({
        nodeReducer: withNodeFlash((node: string, data: Record<string, unknown>) => {
          const state = useUiStore.getState();
          const selId = state.selectedNodeId;
          // Non-geo nodes: resolve visibility via selection or hover
          const nonGeoResult = resolveNonGeoMapVisibility(node, data, selId, fullNeighbors);
          if (nonGeoResult && !nonGeoResult.hidden) return nonGeoResult;
          // If hidden by selection logic, check if neighbor of hovered node
          if (nonGeoResult && nonGeoResult.hidden && state.hoveredNodeId) {
            try {
              if (graph.hasNode(state.hoveredNodeId) && graph.areNeighbors(state.hoveredNodeId, node)) {
                return { ...data, size: 2, zIndex: 1 };
              }
            } catch { /* node may not exist */ }
          }
          if (nonGeoResult) return nonGeoResult;
          // Geo nodes: apply small-node opacity
          const geoResult = applyGeoNodeOpacity(data);
          if (geoResult) return applySelectionHighlight(node, geoResult, selId);
          return applySelectionHighlight(node, data, selId);
        }),
        edgeReducer,
      });
      return;
    }

    // Non-map mode: all nodes visible with selection highlight
    setSettings({
      nodeReducer: withNodeFlash((node: string, data: Record<string, unknown>) => {
        const selId = useUiStore.getState().selectedNodeId;
        return applySelectionHighlight(node, data, selId);
      }),
      edgeReducer,
    });
    // Note: hoveredNodeId is intentionally excluded — the edge reducer reads
    // it from the store at call time to avoid expensive reinstallation on
    // every mouse enter/leave.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQuery, labelMatcher, activePredicateIds, clusterMap, mapEnabled, selectedNodeId, setSettings, sigma]);

  return null;
}
