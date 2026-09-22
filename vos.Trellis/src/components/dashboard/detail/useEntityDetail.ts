/**
 * Assembles what a detail window shows for one root Thing: the configured relations resolved
 * against the model, the derived states of the root and every related Thing, the root's own
 * derived-state change history, and the services the platform ran on it. Nothing here is
 * domain-specific — the relations come from the model's {@link DetailSpec}, and the services from
 * the wiring the platform flags in the model.
 *
 * The states are read from the model store rather than fetched: seeded from the subscription
 * snapshot and followed live, so a card shows a state the moment it moves rather than at the end
 * of the next refresh window. The history and the dispatch stamps are not in the snapshot and are
 * still read.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { relationshipApi } from '../../../api/relationshipApi';
import { stateApi } from '../../../api/stateApi';
import { useThingStates } from '../../../stores/modelStore';
import type { DetailSpecification } from '../../../types/dashboard';
import type { StateHistoryCoverage, VosRelationship, VosThing } from '../../../types/vos';
import {
  resolveRelations,
  buildStateChanges,
  type ResolvedRelation,
  type StateChange,
} from './entityDetail';
import { serviceEdgesOn, dispatchesFrom, type ServiceDispatch } from './serviceHandling';

/** Bound the fan-out so a Thing with a great many dispatches can't fire thousands of requests
 *  for their stamps. */
const MAX_RELATED = 60;

/** The window refreshes on its own cadence rather than following the dashboard's `nonce`.
 *  A round costs a history request, and `nonce` bumps every 400 ms while a sim runs — following it
 *  would issue a request per open window on every flush. */
const REFRESH_THROTTLE_MILLISECONDS = 5000;

export interface EntityDetail {
  loading: boolean;
  root: VosThing | undefined;
  relations: ResolvedRelation[];
  statesById: Map<string, string[]>;
  stateChanges: StateChange[];
  /** Null when the model has no active reactive engine to serve state history. */
  coverage: StateHistoryCoverage | null;
  dispatches: ServiceDispatch[];
}

/** Follow `value` at most once per `milliseconds`, leading-edge: the first change after a quiet period
 *  is taken promptly, then further changes wait out the window. Unlike a debounce this always
 *  makes progress — a continuously-changing value resets a debounce forever, which here would
 *  silently freeze the window's contents for as long as the model kept changing. */
function useThrottled(value: number, milliseconds: number): number {
  const [throttled, setThrottled] = useState(value);
  const lastRun = useRef(0);

  useEffect(() => {
    if (value === throttled) return;
    const wait = Math.max(0, milliseconds - (Date.now() - lastRun.current));
    const timer = setTimeout(() => {
      lastRun.current = Date.now();
      setThrottled(value);
    }, wait);
    return () => clearTimeout(timer);
  }, [value, throttled, milliseconds]);

  return throttled;
}

export function useEntityDetail(
  modelIndex: ModelIndex,
  thingId: string,
  detail: DetailSpecification | undefined,
  nonce = 0,
): EntityDetail {
  const relations = useMemo(() => resolveRelations(thingId, modelIndex, detail?.relations), [thingId, modelIndex, detail]);
  const statesById = useThingStates();
  const serviceEdges = useMemo(() => serviceEdgesOn(thingId, modelIndex).slice(0, MAX_RELATED), [thingId, modelIndex]);

  const historyEnabled = detail?.history?.enabled !== false;
  const refreshTick = useThrottled(nonce, REFRESH_THROTTLE_MILLISECONDS);
  const dispatchKey = serviceEdges.map((edge) => edge.relationshipId).join(',');
  const requestKey = `${thingId}|${dispatchKey}|${refreshTick}`;
  const [resolved, setResolved] = useState<{
    key: string;
    stateChanges: StateChange[];
    coverage: StateHistoryCoverage | null;
    dispatches: ServiceDispatch[];
  }>({
    key: '',
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
      // The history rides alongside the dispatch stamps rather than after them. It is rejected, not
      // thrown, when the model has no active engine — the window still shows its states and relations.
      const [[historyResult], dispatchResults] = await Promise.all([
        Promise.allSettled(historyEnabled ? [stateApi.getStateTransitions(thingId, undefined, undefined, signal)] : []),
        Promise.allSettled(serviceEdges.map((edge) => relationshipApi.get(edge.relationshipId, signal))),
      ]);

      const stamped = new Map<string, VosRelationship>();
      dispatchResults.forEach((result, i) => {
        if (result.status === 'fulfilled') stamped.set(serviceEdges[i].relationshipId, result.value);
      });

      const history = historyResult?.status === 'fulfilled' ? historyResult.value : null;

      if (signal.aborted) return;
      setResolved({
        key: requestKey,
        stateChanges: history ? buildStateChanges(history.Transitions) : [],
        coverage: history?.Coverage ?? null,
        dispatches: dispatchesFrom(serviceEdges, stamped),
      });
    })();

    return () => controller.abort();
    // The relationships are read through requestKey, which the throttle paces; naming them here would refire
    // the round on every model flush.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [thingId, requestKey, historyEnabled]);

  return {
    loading: resolved.key !== requestKey,
    root: modelIndex.byId.get(thingId),
    relations,
    statesById,
    stateChanges: resolved.stateChanges,
    coverage: resolved.coverage,
    dispatches: resolved.dispatches,
  };
}
