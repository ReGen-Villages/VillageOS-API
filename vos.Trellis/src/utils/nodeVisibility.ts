import type Graph from 'graphology';
import type { SearchOptions } from './searchFilter';
import { buildStringMatcher } from './searchFilter';

/** Built on `buildStringMatcher`, so dimming labels agrees with filtering in `filterGraph`. */
export function buildLabelMatcher(
  query: string,
  options: SearchOptions,
): (label: string) => boolean {
  return buildStringMatcher(query, options);
}

export function edgeTouchesNode(
  graph: Graph,
  edge: string,
  nodeId: string | null,
): boolean {
  if (!nodeId) return false;
  return graph.source(edge) === nodeId || graph.target(edge) === nodeId;
}

/** Hover wins, then selection, then an active search, which shows an edge only when both ends match, then
 *  an active predicate filter; with no filter every edge shows. */
type EdgeDisplay = 'show' | 'brighten' | 'hide';

export function decideEdgeDisplay(options: {
  endpointMatchesHover: boolean;
  endpointMatchesSelection: boolean;
  bothEndpointsInSearch: boolean | undefined;
  predicateInActiveFilter: boolean | undefined;
}): EdgeDisplay {
  if (options.endpointMatchesHover) return 'brighten';
  if (options.endpointMatchesSelection) return 'show';
  if (options.bothEndpointsInSearch !== undefined) {
    return options.bothEndpointsInSearch ? 'show' : 'hide';
  }
  if (options.predicateInActiveFilter !== undefined) {
    return options.predicateInActiveFilter ? 'show' : 'hide';
  }
  return 'show';
}
