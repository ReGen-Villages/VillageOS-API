import { useMemo } from 'react';
import type { VosThing, VosRelationship } from '../types/vos';
import { filterGraph, type SearchOptions } from '../utils/searchFilter';

interface UseGraphDataInput {
  things: VosThing[];
  relationships: VosRelationship[];
  searchQuery: string;
  caseSensitive: boolean;
  exactMatch: boolean;
  useRegex: boolean;
}

interface UseGraphDataResult {
  graphThings: VosThing[];
  graphRelationships: VosRelationship[];
  filteredThings: VosThing[];
  filteredRelationships: VosRelationship[];
  matchCount: number;
  searchOptions: SearchOptions;
}

export function useGraphData({
  things,
  relationships,
  searchQuery,
  caseSensitive,
  exactMatch,
  useRegex,
}: UseGraphDataInput): UseGraphDataResult {
  const searchOptions = useMemo(
    () => ({ caseSensitive, exactMatch, useRegex }),
    [caseSensitive, exactMatch, useRegex],
  );

  const { filteredThings, filteredRelationships, matchCount } = useMemo(
    () => filterGraph(searchQuery, things, relationships, searchOptions),
    [searchQuery, searchOptions, things, relationships],
  );

  return {
    graphThings: things,
    graphRelationships: relationships,
    filteredThings,
    filteredRelationships,
    matchCount,
    searchOptions,
  };
}
