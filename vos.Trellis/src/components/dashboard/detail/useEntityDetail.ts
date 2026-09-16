/**
 * Assembles what a detail window shows for one root Thing: the configured relations resolved
 * against the model, the derived states of the root and every related Thing, and the root's own
 * derived-state change history. Nothing here is domain-specific — the relations come from the
 * model's {@link DetailSpec}.
 *
 * The states are read from the model store rather than fetched: seeded from the subscription
 * snapshot and followed live, so a card shows a state the moment it moves rather than at the end
 * of the next refresh window. The history is not in the snapshot and is still read.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { stateApi } from '../../../api/stateApi';
import { useThingStates } from '../../../stores/modelStore';
import type { DetailSpec } from '../../../types/dashboard';
import type { StateHistoryCoverage, VosThing } from '../../../types/vos';
import {
  resolveRelations,
  buildStateChanges,
  type ResolvedRelation,
  type StateChange,
} from './entityDetail';

/** The window refreshes on its own cadence rather than following the dashboard's `nonce`.
 *  A round costs a history request, and `nonce` bumps every 400 ms while a sim runs — following it
 *  would issue a request per open window on every flush. */
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
  const statesById = useThingStates();

  const historyEnabled = detail?.history?.enabled !== false;
  const refreshTick = useThrottled(nonce, REFRESH_THROTTLE_MS);
  const requestKey = `${thingId}|${refreshTick}`;
  const [resolved, setResolved] = useState<{
    key: string;
    stateChanges: StateChange[];
    coverage: StateHistoryCoverage | null;
  }>({
    key: '',
    stateChanges: [],
    coverage: null,
  });

  useEffect(() => {
    // Abandon a superseded round outright: without this a round whose results we would
    // discard anyway keeps competing for connections with the round that replaced it.
    const controller = new AbortController();
    const { signal } = controller;

    (async () => {
      // The history is rejected, not thrown, when the model has no active engine — the window
      // still shows its states and relations.
      const [historyResult] = await Promise.allSettled(
        historyEnabled ? [stateApi.getStateTransitions(thingId, undefined, undefined, signal)] : [],
      );
      const history = historyResult?.status === 'fulfilled' ? historyResult.value : null;

      if (signal.aborted) return;
      setResolved({
        key: requestKey,
        stateChanges: history ? buildStateChanges(history.Transitions) : [],
        coverage: history?.Coverage ?? null,
      });
    })();

    return () => controller.abort();
  }, [thingId, requestKey, historyEnabled]);

  return {
    loading: resolved.key !== requestKey,
    root: idx.byId.get(thingId),
    relations,
    statesById,
    stateChanges: resolved.stateChanges,
    coverage: resolved.coverage,
  };
}
