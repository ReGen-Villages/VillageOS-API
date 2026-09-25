/**
 * The values the decisions a card has open cite, each read as it stood when its own decision was
 * taken.
 *
 * A decision is written once and carries an instant in the past, so a read of one of its cited
 * properties answers the same thing forever. An answer already held is kept and never asked for
 * again: the round follows the decisions on screen rather than the dashboard's refresh, so a card
 * left open through a running model reads each value once, and opening the earlier decisions reads
 * only what they add.
 *
 * Only the decisions on screen are read. Reading every decision a subject keeps is a request per
 * candidate of each, which would have to be capped — and a cap would leave a row saying the platform
 * recorded nothing when the card had simply never asked. What is read is bounded instead by what the
 * model declares for itself: how many decisions a subject keeps, and how many candidates a refusal
 * may name.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import { thingApi } from '../../../api/thingApi';
import {
  instantReadKey,
  instantReadsFor,
  type DecisionExplanation,
  type InstantReadings,
} from './decisionExplanation';

export interface DecisionReadings {
  readings: InstantReadings;
  /** Whether every wanted value has been answered. Until it is, a number is missing because the read
   *  is still out, which is not the same as the platform having nothing to say. */
  settled: boolean;
}

export function useDecisionExplanations(decisions: DecisionExplanation[]): DecisionReadings {
  const wanted = useMemo(() => instantReadsFor(decisions), [decisions]);
  const wantedKeys = wanted.map((read) => instantReadKey(read.thingId, read.instant)).join(',');

  // The same answers twice over: the ref is what the round reads to know which are still missing,
  // before any request goes out, and the state is what a render reads.
  const held = useRef<InstantReadings>(new Map());
  const [readings, setReadings] = useState<InstantReadings>(new Map());

  useEffect(() => {
    const missing = wanted.filter((read) => !held.current.has(instantReadKey(read.thingId, read.instant)));
    if (!missing.length) return;

    const controller = new AbortController();
    (async () => {
      const results = await Promise.allSettled(
        missing.map((read) => thingApi.getAtInstant(read.thingId, read.instant, controller.signal)),
      );
      if (controller.signal.aborted) return;

      // Keyed on the wanted reads rather than merged into what is already held, so a card left open
      // on a subject decided about again and again keeps only what the decisions on screen cite. A
      // round runs whenever a read is wanted that is not held, which is every time the set grows.
      const answers: InstantReadings = new Map();
      for (const read of wanted) {
        const key = instantReadKey(read.thingId, read.instant);
        if (held.current.has(key)) answers.set(key, held.current.get(key) ?? null);
      }
      results.forEach((result, position) => {
        const { thingId, instant } = missing[position];
        answers.set(instantReadKey(thingId, instant), result.status === 'fulfilled' ? result.value : null);
      });
      held.current = answers;
      setReadings(answers);
    })();

    return () => controller.abort();
    // The reads are named by wantedKeys, which changes only when the decisions on screen do; naming
    // the array itself would refire the round on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wantedKeys]);

  return {
    readings,
    settled: wanted.every((read) => readings.has(instantReadKey(read.thingId, read.instant))),
  };
}
