/**
 * Fetches everything a detail window shows for one root Thing: the configured relations resolved
 * against the model, the derived states of the root and every related Thing, and the root's own
 * derived-state change history. All sources are existing generic endpoints; nothing here is
 * domain-specific — the relations come from the model's {@link DetailSpec}.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { stateApi } from '../../../api/stateApi';
import type { DetailSpec } from '../../../types/dashboard';
import type { StateHistoryCoverage, VosThing } from '../../../types/vos';
import {
  resolveRelations,
  flattenRelatedIds,
  buildStateChanges,
  type ResolvedRelation,
  type StateChange,
} from './entityDetail';

/** Bound the fan-out so a pathologically large order can't fire thousands of state requests. */
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

  const historyEnabled = detail?.history?.enabled !== false;
  const refreshTick = useThrottled(nonce, REFRESH_THROTTLE_MS);
  const requestKey = `${allIds.join(',')}|${refreshTick}`;
  const [resolved, setResolved] = useState<{
    key: string;
    statesById: Map<string, string[]>;
    stateChanges: StateChange[];
    coverage: StateHistoryCoverage | null;
  }>({
    key: '',
    statesById: new Map(),
    stateChanges: [],
    coverage: null,
  });

  useEffect(() => {
    // Abandon a superseded round outright: without this a round whose results we would
    // discard anyway keeps competing for connections with the round that replaced it.
    const controller = new AbortController();
    const { signal } = controller;

    (async () => {
      // The root's state history rides alongside the current-state fan-out rather than after it.
      // It is rejected, not thrown, when the model has no active engine — the rest still resolves.
      const [stateResults, [historyResult]] = await Promise.all([
        Promise.allSettled(allIds.map((id) => stateApi.getThingStates(id, signal))),
        Promise.allSettled(historyEnabled ? [stateApi.getStateTransitions(thingId, undefined, undefined, signal)] : []),
      ]);

      const statesById = new Map<string, string[]>();
      stateResults.forEach((result, i) => {
        if (result.status === 'fulfilled') statesById.set(allIds[i], result.value.CurrentStates ?? []);
      });

      const history = historyResult?.status === 'fulfilled' ? historyResult.value : null;

      if (signal.aborted) return;
      setResolved({
        key: requestKey,
        statesById,
        stateChanges: history ? buildStateChanges(history.Transitions) : [],
        coverage: history?.Coverage ?? null,
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
  };
}
