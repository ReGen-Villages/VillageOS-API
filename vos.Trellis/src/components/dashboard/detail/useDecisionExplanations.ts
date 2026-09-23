/**
 * The decisions a card shows for one Thing, with every property a constraint names read as it stood
 * when the decision was taken.
 *
 * A decision is written once and carries an instant in the past, so a read of one of its cited
 * properties answers the same thing forever. The round therefore follows the set of decisions rather
 * than the dashboard's refresh: a card left open through a running model reads each value once.
 */
import { useEffect, useMemo, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { thingApi } from '../../../api/thingApi';
import {
  decisionsOn,
  instantReadKey,
  instantReadsFor,
  type DecisionExplanation,
  type InstantReadings,
} from './decisionExplanation';

/** Bound the fan-out so a Thing decided about many times cannot fire a request per candidate of every
 *  decision it ever carried. */
const MOST_THINGS_READ_AT_AN_INSTANT = 60;

export interface DecisionReadings {
  decisions: DecisionExplanation[];
  readings: InstantReadings;
  /** Whether every wanted value has been answered. Until it is, a number is missing because the read
   *  is still out, which is not the same as the platform having nothing to say. */
  settled: boolean;
}

export function useDecisionExplanations(thingId: string, modelIndex: ModelIndex): DecisionReadings {
  const decisions = useMemo(() => decisionsOn(thingId, modelIndex), [thingId, modelIndex]);
  const wanted = useMemo(
    () => instantReadsFor(decisions).slice(0, MOST_THINGS_READ_AT_AN_INSTANT),
    [decisions],
  );
  const wantedKeys = wanted.map((read) => instantReadKey(read.thingId, read.instant)).join(',');
  const [answered, setAnswered] = useState<{ keys: string; readings: InstantReadings }>({
    keys: '',
    readings: new Map(),
  });

  useEffect(() => {
    if (!wanted.length) return;

    const controller = new AbortController();
    (async () => {
      const results = await Promise.allSettled(
        wanted.map((read) => thingApi.getAtInstant(read.thingId, read.instant, controller.signal)),
      );
      if (controller.signal.aborted) return;

      const readings: InstantReadings = new Map();
      results.forEach((result, position) => {
        const { thingId: readThingId, instant } = wanted[position];
        readings.set(instantReadKey(readThingId, instant), result.status === 'fulfilled' ? result.value : null);
      });
      setAnswered({ keys: wantedKeys, readings });
    })();

    return () => controller.abort();
    // The reads are named by wantedKeys, which changes only when the set of decisions does; naming
    // the array itself would refire the round on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wantedKeys]);

  // An answer already held stays held when a new decision arrives: both halves of its key are
  // written once, so what it says cannot go stale, and the rows already on the card keep their
  // numbers while the new decision's reads are out.
  return {
    decisions,
    readings: answered.readings,
    settled: wanted.length === 0 || answered.keys === wantedKeys,
  };
}
