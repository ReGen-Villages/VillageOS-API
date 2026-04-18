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
