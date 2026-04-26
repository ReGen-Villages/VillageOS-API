import type Graph from 'graphology';
import type { SearchOptions } from './searchFilter';
import { buildStringMatcher } from './searchFilter';

/** Check whether a thing is a containment predicate (stamped by the IFC importer). */
export function isContainmentPredicate(props: Record<string, unknown> | undefined): boolean {
  return props?.__IsMapContainmentPredicate === true;
}

/**
 * Build a label matcher for the Sigma nodeReducer.
 * Delegates to the shared `buildStringMatcher` so label-level dimming
 * stays consistent with the data-level filtering in `filterGraph`.
 */
export function buildLabelMatcher(
  query: string,
  options: SearchOptions,
): (label: string) => boolean {
  return buildStringMatcher(query, options);
}

/** Check if an edge touches the given node. */
export function edgeTouchesNode(
  graph: Graph,
  edge: string,
  nodeId: string | null,
): boolean {
  if (!nodeId) return false;
  return graph.source(edge) === nodeId || graph.target(edge) === nodeId;
}

/**
 * Pure-function decision for an edge's display state given the current
 * filter context. Extracted from <c>NodeReducer</c> so the policy is
 * unit-testable (Feature #5344).
 *
 * Resolution order:
 *   1. Hover takes precedence  → 'brighten' if the edge touches the hovered node
 *   2. Selection                 → 'show' if the edge touches the selected node
 *   3. Search active             → 'show' iff both endpoints are matched, else 'hide'
 *   4. Predicate filter active   → 'show' iff this edge's predicate is selected, else 'hide'
 *   5. No filter active          → respect <c>showAllByDefault</c>: 'show' when true
 *                                  (every edge visible — the user-friendly default),
 *                                  'hide' when false (the dense-graph quiet mode)
 */
export type EdgeDisplay = 'show' | 'brighten' | 'hide';

export function decideEdgeDisplay(opts: {
  endpointMatchesHover: boolean;
  endpointMatchesSelection: boolean;
  bothEndpointsInSearch: boolean | undefined;
  predicateInActiveFilter: boolean | undefined;
  showAllByDefault: boolean;
}): EdgeDisplay {
  if (opts.endpointMatchesHover) return 'brighten';
  if (opts.endpointMatchesSelection) return 'show';
  if (opts.bothEndpointsInSearch !== undefined) {
    return opts.bothEndpointsInSearch ? 'show' : 'hide';
  }
  if (opts.predicateInActiveFilter !== undefined) {
    return opts.predicateInActiveFilter ? 'show' : 'hide';
  }
  return opts.showAllByDefault ? 'show' : 'hide';
}
