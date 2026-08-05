import { useState, useEffect, useRef, useCallback } from 'react';
import type { ThingRangesResponse, ThingStates } from '../types/vos';
import type { RelationshipRangesEntry } from '../components/panels/RangesTabContent';
import { rangeApi } from '../api/rangeApi';

/**
 * Lazily fetches a composite range summary when the 'ranges' tab is active.
 * Point-in-time: statesVersion is snapshotted when the tab opens or thingId
 * changes, so live SSE pushes are ignored until the user navigates away
 * and back or selects a different node.
 */
export function useNodeRangesData(
  thingId: string,
  tab: string,
  statesVersion: number | undefined,
) {
  const [rangesData, setRangesData] = useState<ThingRangesResponse | null>(null);
  const [statesData, setStatesData] = useState<ThingStates | null>(null);
  const [relRangesEntries, setRelRangesEntries] = useState<RelationshipRangesEntry[]>([]);

  const [snapshot, setSnapshot] = useState<number | undefined>(undefined);
  const [refreshKey, setRefreshKey] = useState(0);

  // Which request the tab is showing an answer for. Loading is read off that rather than stored:
  // raising a flag from inside the effect renders once without it and once with it, so the tab
  // paints "nothing to show" for a frame before the spinner appears.
  const request = `${thingId}:${snapshot ?? ''}:${refreshKey}`;
  const [answeredRequest, setAnsweredRequest] = useState<string | null>(null);
  const rangesLoading = tab === 'ranges' && snapshot !== undefined && answeredRequest !== request;
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

  useEffect(() => {
    if (tab !== 'ranges' || snapshot === undefined) return;
    let cancelled = false;
    (async () => {
      try {
        const summary = await rangeApi.getSummary(thingId);
        if (cancelled) return;

        setRangesData({
          ThingId: summary.ObjectId,
          ThingName: summary.ObjectName,
          OwnRanges: summary.OwnRanges,
          InheritedRanges: summary.InheritedRanges,
        });
        setStatesData({
          ThingId: summary.ObjectId,
          ThingName: summary.ObjectName,
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
        if (!cancelled) setAnsweredRequest(request);
      }
    })();
    return () => { cancelled = true; };
  }, [thingId, tab, snapshot, refreshKey, request]);

  const refresh = useCallback(() => setRefreshKey((k) => k + 1), []);

  return {
    rangesData,
    statesData,
    rangesLoading,
    relRangesEntries,
    refresh,
  };
}
