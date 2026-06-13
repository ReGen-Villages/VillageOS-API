import { useState, useEffect, useRef, useCallback } from 'react';
import type { ThingRangesResponse, ThingStates } from '../types/vos';
import type { RelationshipRangesEntry } from '../components/panels/RangesTabContent';
import { rangeApi } from '../api/rangeApi';

/**
 * Hook that lazily fetches a composite range summary for a thing and all its
 * relationships in a single API call when the 'ranges' tab is active.
 *
 * Uses a temporal snapshot approach: statesVersion is captured when the tab
 * opens (or when thingId changes), and all fetches use that snapshot.
 * Continuous SignalR pushes are ignored until the user navigates away and back
 * or selects a different node.
 */
export function useNodeRangesData(
  thingId: string,
  tab: string,
  statesVersion: number | undefined,
) {
  const [rangesData, setRangesData] = useState<ThingRangesResponse | null>(null);
  const [statesData, setStatesData] = useState<ThingStates | null>(null);
  const [rangesLoading, setRangesLoading] = useState(false);
  const [relRangesEntries, setRelRangesEntries] = useState<RelationshipRangesEntry[]>([]);

  // Snapshot statesVersion when the ranges tab becomes active or thingId changes.
  // This gives us a point-in-time view — mutations arriving via SignalR after the
  // snapshot don't trigger re-fetches until the user navigates away and back.
  const [snapshot, setSnapshot] = useState<number | undefined>(undefined);
  const [refreshKey, setRefreshKey] = useState(0);
  const prevThingId = useRef('');
  const prevTab = useRef('');

  useEffect(() => {
    const thingChanged = thingId !== prevThingId.current;
    const tabJustOpened = tab === 'ranges' && prevTab.current !== 'ranges';
    prevThingId.current = thingId;
    prevTab.current = tab;

    if (tab === 'ranges' && (thingChanged || tabJustOpened)) {
      setSnapshot(statesVersion);
    }
  }, [thingId, tab, statesVersion]);

  // Single fetch: range-summary returns thing ranges, states, and relationship data
  useEffect(() => {
    if (tab !== 'ranges' || snapshot === undefined) return;
    let cancelled = false;
    setRangesLoading(true);
    (async () => {
      try {
        const summary = await rangeApi.getSummary(thingId);
        if (cancelled) return;

        setRangesData({
          ThingId: summary.ThingId,
          ThingName: summary.ThingName,
          OwnRanges: summary.OwnRanges,
          InheritedRanges: summary.InheritedRanges,
        });
        setStatesData({
          ThingId: summary.ThingId,
          ThingName: summary.ThingName,
          CurrentStates: summary.CurrentStates,
          RangeEvaluations: summary.RangeEvaluations,
          OutOfBoundsCount: summary.OutOfBoundsCount,
        });
        setRelRangesEntries(summary.Relationships.map((rel) => ({
          relationshipId: rel.RelationshipId,
          relationshipName: rel.RelationshipName,
          label: `${rel.SubjectName} → ${rel.PredicateName} → ${rel.TargetName}`,
          rangesData: {
            ThingId: rel.RelationshipId,
            ThingName: rel.RelationshipName,
            OwnRanges: rel.OwnRanges,
            InheritedRanges: [],
          },
          statesData: {
            ThingId: rel.RelationshipId,
            ThingName: rel.RelationshipName,
            CurrentStates: rel.CurrentStates,
            RangeEvaluations: rel.RangeEvaluations,
            OutOfBoundsCount: rel.OutOfBoundsCount,
          },
        })));
      } catch {
        if (!cancelled) {
          setRangesData(null);
          setStatesData(null);
          setRelRangesEntries([]);
        }
      } finally {
        if (!cancelled) setRangesLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [thingId, tab, snapshot, refreshKey]);

  const refresh = useCallback(() => setRefreshKey((k) => k + 1), []);

  return {
    rangesData,
    statesData,
    rangesLoading,
    relRangesEntries,
    refresh,
  };
}
