/**
 * Fetches everything a detail window shows for one root Thing: the configured relations resolved
 * against the model, the derived states of the root and every related Thing, the root's own
 * derived-state change history, and the services the platform ran on it. All sources are existing
 * generic endpoints; nothing here is domain-specific — the relations come from the model's
 * {@link DetailSpec}, and the services from the wiring the platform flags in the model.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { relationshipApi } from '../../../api/relationshipApi';
import { stateApi } from '../../../api/stateApi';
import type { DetailSpec } from '../../../types/dashboard';
import type { StateHistoryCoverage, VosRelationship, VosThing } from '../../../types/vos';
import {
  resolveRelations,
  flattenRelatedIds,
  buildStateChanges,
  type ResolvedRelation,
  type StateChange,
} from './entityDetail';
import { serviceEdgesOn, dispatchesFrom, type ServiceDispatch } from './serviceHandling';

/** Bound the fan-out so a Thing with a great many related Things or dispatches can't fire
 *  thousands of requests, whether for a related Thing's states or for a dispatch stamp. */
const MAX_RELATED = 60;

/** The window refreshes on its own cadence rather than following the dashboard's `nonce`.
 *  One round costs a request per related Thing, and `nonce` bumps every 400 ms while a sim runs —
 *  following it would issue a burst of requests per open window on every flush. */
const REFRESH_THROTTLE_MS = 5000;

export interface EntityDetail {
  loading: boolean;
  root: VosThing | undefined;
  relations: ResolvedRelation[];
  statesById: Map<string, string[]>;
  stateChanges: StateChange[];
  /** Null when the model has no active reactive engine to serve state history. */
  coverage: StateHistoryCoverage | null;
  /** The services the platform dispatched on this Thing, oldest first. */
  dispatches: ServiceDispatch[];
}

/** Follow `value` at most once per `ms`, leading-edge: the first change after a quiet period
 *  is taken promptly, then further changes wait out the window. Unlike a debounce this always
 *  makes progress — a continuously-changing value resets a debounce forever, which here would
 *  silently freeze the window's contents for as long as the model kept changing. */
function useThrottled(value: number, ms: number): number {
  const [throttled, setThrottled] = useState(value);
  const lastRun = useRef(0);

  useEffect(() => {
    if (value === throttled) return;
    const wait = Math.max(0, ms - (Date.now() - lastRun.current));
    const timer = setTimeout(() => {
      lastRun.current = Date.now();
      setThrottled(value);
    }, wait);
    return () => clearTimeout(timer);
  }, [value, throttled, ms]);

  return throttled;
}

export function useEntityDetail(
  idx: ModelIndex,
  thingId: string,
  detail: DetailSpec | undefined,
  nonce = 0,
): EntityDetail {
  const relations = useMemo(() => resolveRelations(thingId, idx, detail?.relations), [thingId, idx, detail]);
  const relatedIds = useMemo(() => flattenRelatedIds(relations).slice(0, MAX_RELATED), [relations]);
  const allIds = useMemo(() => [thingId, ...relatedIds], [thingId, relatedIds]);
  const serviceEdges = useMemo(() => serviceEdgesOn(thingId, idx).slice(0, MAX_RELATED), [thingId, idx]);

  const historyEnabled = detail?.history?.enabled !== false;
  const refreshTick = useThrottled(nonce, REFRESH_THROTTLE_MS);
  const dispatchKey = serviceEdges.map((edge) => edge.relationshipId).join(',');
  const requestKey = `${allIds.join(',')}|${dispatchKey}|${refreshTick}`;
  const [resolved, setResolved] = useState<{
    key: string;
    statesById: Map<string, string[]>;
    stateChanges: StateChange[];
    coverage: StateHistoryCoverage | null;
    dispatches: ServiceDispatch[];
  }>({
    key: '',
    statesById: new Map(),
    stateChanges: [],
    coverage: null,
    dispatches: [],
  });

  useEffect(() => {
    // Abandon a superseded round outright: without this a round whose results we would
    // discard anyway keeps competing for connections with the round that replaced it.
    const controller = new AbortController();
    const { signal } = controller;

    (async () => {
      // The root's state history rides alongside the current-state fan-out rather than after it.
      // It is rejected, not thrown, when the model has no active engine — the rest still resolves.
      const [stateResults, [historyResult], dispatchResults] = await Promise.all([
        Promise.allSettled(allIds.map((id) => stateApi.getThingStates(id, signal))),
        Promise.allSettled(historyEnabled ? [stateApi.getStateTransitions(thingId, undefined, undefined, signal)] : []),
        Promise.allSettled(serviceEdges.map((edge) => relationshipApi.get(edge.relationshipId, signal))),
      ]);

      const statesById = new Map<string, string[]>();
      stateResults.forEach((result, i) => {
        if (result.status === 'fulfilled') statesById.set(allIds[i], result.value.CurrentStates ?? []);
      });

      const stamped = new Map<string, VosRelationship>();
      dispatchResults.forEach((result, i) => {
        if (result.status === 'fulfilled') stamped.set(serviceEdges[i].relationshipId, result.value);
      });

      const history = historyResult?.status === 'fulfilled' ? historyResult.value : null;

      if (signal.aborted) return;
      setResolved({
        key: requestKey,
        statesById,
        stateChanges: history ? buildStateChanges(history.Transitions) : [],
        coverage: history?.Coverage ?? null,
        dispatches: dispatchesFrom(serviceEdges, stamped),
      });
    })();

    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [thingId, requestKey, historyEnabled]);

  return {
    loading: resolved.key !== requestKey,
    root: idx.byId.get(thingId),
    relations,
    statesById: resolved.statesById,
    stateChanges: resolved.stateChanges,
    coverage: resolved.coverage,
    dispatches: resolved.dispatches,
  };
}
