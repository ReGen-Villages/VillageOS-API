import { useMemo } from 'react';
import type { VosThing, VosRelationship } from '../types/vos';
import { filterGraph, type SearchOptions } from '../utils/searchFilter';
import { isContainmentPredicate } from '../utils/nodeVisibility';

interface UseGraphDataInput {
  things: VosThing[];
  relationships: VosRelationship[];
  searchQuery: string;
  caseSensitive: boolean;
  exactMatch: boolean;
  useRegex: boolean;
  mapEnabled: boolean;
  showAllThings: boolean;
  hideOrphanSites: boolean;
}

interface UseGraphDataResult {
  graphThings: VosThing[];
  graphRelationships: VosRelationship[];
  filteredThings: VosThing[];
  filteredRelationships: VosRelationship[];
  matchCount: number;
  hasGeoNodes: boolean;
  searchOptions: SearchOptions;
}

/**
 * Computes the filtered graph data for the Graph page. Encapsulates map-mode
 * reduction (surface-only vs all-things vs orphan-hidden), search filtering,
 * and geo-node detection in a single hook so GraphPage doesn't carry the
 * computation weight.
 */
export function useGraphData({
  things,
  relationships,
  searchQuery,
  caseSensitive,
  exactMatch,
  useRegex,
  mapEnabled,
  showAllThings,
  hideOrphanSites,
}: UseGraphDataInput): UseGraphDataResult {
  const hasGeoNodes = useMemo(
    () =>
      things.some(
        (t) =>
          (typeof t.Properties?.latitude === 'number' &&
            typeof t.Properties?.longitude === 'number') ||
          t.Properties?.geometry != null,
      ),
    [things],
  );

  const { graphThings, graphRelationships } = useMemo(() => {
    if (!mapEnabled) return { graphThings: things, graphRelationships: relationships };

    if (showAllThings) {
      const geoIds = new Set<string>();
      for (const t of things) {
        if (t.Properties?.geometry != null || t.Properties?.footprint != null) {
          geoIds.add(t.Id);
        }
      }
      const keepIds = new Set(geoIds);
      for (const r of relationships) keepIds.add(r.PredicateId);
      const thingNameMap = new Map(things.map((t) => [t.Id, t.Name]));
      for (const r of relationships) {
        const predName = thingNameMap.get(r.PredicateId)?.toLowerCase();
        if (predName === 'is' && geoIds.has(r.SubjectId)) {
          keepIds.add(r.TargetId);
        }
      }
      const gt = things.filter((t) => keepIds.has(t.Id));
      const gr = relationships.filter((r) => keepIds.has(r.SubjectId) && keepIds.has(r.TargetId));
      return { graphThings: gt, graphRelationships: gr };
    }

    // Surface things have __IsSurface=true (stamped by broker at seed load).
    // When hideOrphanSites is on (default), filter further to __IsMapSurfaceThing:
    // only things inside the primary IfcSite's spatial subtree, which the IFC
    // importer stamps directly during its native spatial walk. This hides
    // Revit template-default sites that come attached to imported family
    // instances but aren't part of the real project's spatial hierarchy.
    const surfaceIds = new Set<string>();
    for (const t of things) {
      const included = hideOrphanSites
        ? t.Properties?.__IsMapSurfaceThing === true
        : t.Properties?.__IsSurface === true;
      if (included) {
        surfaceIds.add(t.Id);
      }
    }

    // Also keep predicate things and type targets (for color classification).
    // Skip containment predicates — those are thousands of IFC sub-elements.
    const keepIds = new Set(surfaceIds);
    for (const r of relationships) keepIds.add(r.PredicateId);

    // Non-geo neighbors of surface nodes via non-containment predicates.
    const thingMap = new Map(things.map((t) => [t.Id, t]));
    const skipPredicates = new Set<string>();
    for (const [id, t] of thingMap) {
      if (isContainmentPredicate(t.Properties)) skipPredicates.add(id);
    }
    for (const r of relationships) {
      if (skipPredicates.has(r.PredicateId)) continue;
      if (surfaceIds.has(r.SubjectId)) keepIds.add(r.TargetId);
      if (surfaceIds.has(r.TargetId)) keepIds.add(r.SubjectId);
    }
    const gt = things.filter((t) => keepIds.has(t.Id));
    const gr = relationships.filter(
      (r) => keepIds.has(r.SubjectId) && keepIds.has(r.TargetId),
    );

    return { graphThings: gt, graphRelationships: gr };
  }, [mapEnabled, showAllThings, hideOrphanSites, things, relationships]);

  const searchOptions = useMemo(
    () => ({ caseSensitive, exactMatch, useRegex }),
    [caseSensitive, exactMatch, useRegex],
  );

  const { filteredThings, filteredRelationships, matchCount } = useMemo(
    () => filterGraph(searchQuery, graphThings, graphRelationships, searchOptions),
    [searchQuery, searchOptions, graphThings, graphRelationships],
  );

  return {
    graphThings,
    graphRelationships,
    filteredThings,
    filteredRelationships,
    matchCount,
    hasGeoNodes,
    searchOptions,
  };
}
