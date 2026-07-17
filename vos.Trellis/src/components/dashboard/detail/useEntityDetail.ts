/**
 * Fetches everything a detail window shows for one root Thing: the derived states of the
 * root and every involved Thing, and a chronological handling-history timeline (property
 * changes attributed to their writing service + relationship movements). All sources are
 * existing generic endpoints; nothing here is domain-specific.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { stateApi } from '../../../api/stateApi';
import { temporalApi } from '../../../api/temporalApi';
import type { DetailSpec } from '../../../types/dashboard';
import type { PropertyFact, StateTransitionsResponse, ThingMutations, VosThing } from '../../../types/vos';
import { collectInvolved, mergeTimeline, type MovementInput, type TimelineEvent } from './entityDetail';

/** Bound the fan-out so a pathologically large root Thing can't fire thousands of requests. */
const MAX_INVOLVED = 40;
const MAX_FACT_LOOKUPS = 80;

/** The window refreshes on its own cadence rather than following the dashboard's `nonce`.
 *  One round costs a request per involved Thing plus its Fact lookups, and `nonce` bumps
 *  every 400 ms while a sim runs — following it would issue hundreds of requests a second
 *  per open window, trading the CPU cost #5958 removed for a network one. */
const REFRESH_THROTTLE_MS = 5000;

type MovementEdge = MovementInput & { relId: string };

export interface EntityDetail {
  loading: boolean;
  root: VosThing | undefined;
  involvedIds: string[];
  statesById: Map<string, string[]>;
  timeline: TimelineEvent[];
  /** Null when the model has no active reactive engine to serve state history. */
  stateHistory: StateTransitionsResponse | null;
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
  const involvedIds = useMemo(
    () => collectInvolved(thingId, idx, detail?.involves).slice(0, MAX_INVOLVED),
    [thingId, idx, detail],
  );
  const allIds = useMemo(() => [thingId, ...involvedIds], [thingId, involvedIds]);

  const movementEdges = useMemo<MovementEdge[]>(() => {
    const inSet = new Set(allIds);
    const movementIds = detail?.movementPredicates?.length
      ? new Set(
          detail.movementPredicates
            .map((name) => idx.predicateNameToId.get(name))
            .filter((id): id is string => !!id),
        )
      : null;
    const out: MovementEdge[] = [];
    for (const rel of idx.relationships) {
      if (!inSet.has(rel.SubjectId) || !inSet.has(rel.TargetId)) continue;
      if (movementIds && !movementIds.has(rel.PredicateId)) continue;
      out.push({
        relId: rel.Id,
        predicate: idx.predicateIdToName.get(rel.PredicateId) ?? 'related',
        subjectId: rel.SubjectId,
        subjectName: idx.byId.get(rel.SubjectId)?.Name ?? rel.SubjectId,
        targetId: rel.TargetId,
        targetName: idx.byId.get(rel.TargetId)?.Name ?? rel.TargetId,
        time: null,
      });
    }
    return out;
  }, [allIds, idx, detail]);

  // Key the fetch on the involved membership + the throttled live signal. State is written
  // only from the async callback and `loading` derived from a key match (the shape useBinding
  // uses). `movementEdges` churns identity on every flush without changing content, so it is
  // read through a ref rather than being a dependency.
  const refreshTick = useThrottled(nonce, REFRESH_THROTTLE_MS);
  const requestKey = `${allIds.join(',')}|${refreshTick}`;
  const [resolved, setResolved] = useState<{
    key: string;
    statesById: Map<string, string[]>;
    timeline: TimelineEvent[];
    stateHistory: StateTransitionsResponse | null;
  }>({
    key: '',
    statesById: new Map(),
    timeline: [],
    stateHistory: null,
  });

  // Declared before the fetch effect so the edges are current by the time a round starts.
  const edgesRef = useRef(movementEdges);
  useEffect(() => {
    edgesRef.current = movementEdges;
  }, [movementEdges]);

  useEffect(() => {
    // Abandon a superseded round outright: without this a round whose results we would
    // discard anyway keeps competing for connections with the round that replaced it.
    const controller = new AbortController();
    const { signal } = controller;

    (async () => {
      // The root's state history rides alongside the current-state fan-out rather than after it.
      // It is rejected, not thrown, when the model has no active engine — the rest of the window
      // still resolves.
      const [stateResults, [historyResult]] = await Promise.all([
        Promise.allSettled(allIds.map((id) => stateApi.getThingStates(id, signal))),
        Promise.allSettled([stateApi.getStateTransitions(thingId, undefined, undefined, signal)]),
      ]);
      const statesById = new Map<string, string[]>();
      stateResults.forEach((result, i) => {
        if (result.status === 'fulfilled') statesById.set(allIds[i], result.value.CurrentStates ?? []);
      });
      const stateHistory = historyResult.status === 'fulfilled' ? historyResult.value : null;

      const mutationResults = await Promise.allSettled(
        allIds.map((id) => temporalApi.getThingMutations(id, undefined, undefined, signal)),
      );
      const mutations: ThingMutations[] = mutationResults
        .filter((r): r is PromiseFulfilledResult<ThingMutations> => r.status === 'fulfilled')
        .map((r) => r.value);

      // Movement times from each edge's earliest recorded change (best-effort).
      const movements = await Promise.all(
        edgesRef.current.map(async (edge) => {
          try {
            const relMutations = await temporalApi.getRelationshipMutations(edge.relId, undefined, undefined, signal);
            const times = relMutations.Mutations.map((m) => m.Timestamp).filter(Boolean).sort();
            return { ...edge, time: times[0] ?? null };
          } catch {
            return edge;
          }
        }),
      );

      // Author attribution: the service that wrote each changed (Thing, property), capped.
      const keys: { id: string; property: string }[] = [];
      const seenKeys = new Set<string>();
      for (const thing of mutations) {
        for (const mutation of thing.Mutations) {
          const key = `${thing.ObjectId}::${mutation.PropertyName}`;
          if (seenKeys.has(key)) continue;
          seenKeys.add(key);
          keys.push({ id: thing.ObjectId, property: mutation.PropertyName });
        }
      }
      const factsByKey = new Map<string, PropertyFact[]>();
      const capped = keys.slice(0, MAX_FACT_LOOKUPS);
      const factResults = await Promise.allSettled(
        capped.map((k) => temporalApi.getPropertyFacts(k.id, k.property, signal)),
      );
      factResults.forEach((result, i) => {
        if (result.status === 'fulfilled') {
          factsByKey.set(`${capped[i].id}::${capped[i].property}`, result.value.entries ?? []);
        }
      });

      if (signal.aborted) return;
      setResolved({ key: requestKey, statesById, timeline: mergeTimeline({ mutations, movements, factsByKey }), stateHistory });
    })();

    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [thingId, requestKey]);

  return {
    loading: resolved.key !== requestKey,
    root: idx.byId.get(thingId),
    involvedIds,
    statesById: resolved.statesById,
    timeline: resolved.timeline,
    stateHistory: resolved.stateHistory,
  };
}
