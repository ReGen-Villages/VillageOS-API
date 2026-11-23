import { useState, useEffect, useRef, useCallback } from 'react';
import type { ThingRangesResponse, ThingStates } from '@tendril-ui/tendril';
import type { RelationshipRangesEntry } from '../components/panels/RangesTabContent';
import { rangeApi } from '../api/rangeApi';

/** Read only while the ranges tab is open, once per opening and per Thing: live changes are ignored until
 *  the person opens the tab again or picks another Thing. */
export function useNodeRangesData(
  thingId: string,
  tab: string,
  statesVersion: number | undefined,
) {
  const [rangesData, setRangesData] = useState<ThingRangesResponse | null>(null);
  const [statesData, setStatesData] = useState<ThingStates | null>(null);
  const [relationshipRangesEntries, setRelationshipRangesEntries] = useState<RelationshipRangesEntry[]>([]);

  const [snapshot, setSnapshot] = useState<number | undefined>(undefined);
  const [refreshKey, setRefreshKey] = useState(0);

  // Which request the tab is showing an answer for. Loading is read off that rather than stored:
  // raising a flag from inside the effect renders once without it and once with it, so the tab
  // paints "nothing to show" for a frame before the spinner appears.
  const request = `${thingId}:${snapshot ?? ''}:${refreshKey}`;
  const [answeredRequest, setAnsweredRequest] = useState<string | null>(null);
  const rangesLoading = tab === 'ranges' && snapshot !== undefined && answeredRequest !== request;
  const previousThingId = useRef('');
  const previousTab = useRef('');

  useEffect(() => {
    const thingChanged = thingId !== previousThingId.current;
    const tabJustOpened = tab === 'ranges' && previousTab.current !== 'ranges';
    previousThingId.current = thingId;
    previousTab.current = tab;

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
        setRelationshipRangesEntries(summary.Relationships.map((relationship) => ({
          relationshipId: relationship.RelationshipId,
          relationshipName: relationship.RelationshipName,
          label: `${relationship.SubjectName} → ${relationship.PredicateName} → ${relationship.TargetName}`,
          rangesData: {
            ThingId: relationship.RelationshipId,
            ThingName: relationship.RelationshipName,
            OwnRanges: relationship.OwnRanges,
            InheritedRanges: [],
          },
          statesData: {
            ThingId: relationship.RelationshipId,
            ThingName: relationship.RelationshipName,
            CurrentStates: relationship.CurrentStates,
            RangeEvaluations: relationship.RangeEvaluations,
            OutOfBoundsCount: relationship.OutOfBoundsCount,
          },
        })));
      } catch {
        if (!cancelled) {
          setRangesData(null);
          setStatesData(null);
          setRelationshipRangesEntries([]);
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
    relationshipRangesEntries,
    refresh,
  };
}
