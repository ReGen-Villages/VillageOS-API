import { useEffect, useRef, useState, type RefObject } from 'react';
import { resolveBinding, type BindingResult, type ModelIndex, type ResolveContext } from '../api/dashboardApi';
import { askedOfTheBroker, statesRead } from '../api/bindingRefresh';
import type { Binding } from '../types/dashboard';

/** A number naming one index, so what a binding waits for is one comparable value whether its
 *  answer comes from the loaded model or from the broker. */
const generations = new WeakMap<ModelIndex, number>();
let generationsGiven = 0;

function generationOf(index: ModelIndex): number {
  const known = generations.get(index);
  if (known !== undefined) return known;
  generationsGiven += 1;
  generations.set(index, generationsGiven);
  return generationsGiven;
}

/** The newest context, for an effect that must resolve against it without re-running whenever it
 *  is replaced. Written in an effect of its own, declared first, so it is current before any effect
 *  below it reads it. */
function useLatest(context: ResolveContext): RefObject<ResolveContext> {
  const held = useRef(context);
  useEffect(() => {
    held.current = context;
  });
  return held;
}

function waitOf(binding: Binding, context: ResolveContext): string {
  if (!askedOfTheBroker(binding)) return `model:${generationOf(context.index)}`;
  const versions = context.stateVersions ?? {};
  const states = statesRead(binding).map((state) => `${state}=${versions[state] ?? 0}`);
  return `broker:${context.serverRefresh ?? 0}:${context.loadsApplied}:${states.join(',')}`;
}

/** What these bindings are waiting for, as one value that moves when — and only when — one of them
 *  has to be resolved again. A figure read from the loaded model follows that model; a figure the
 *  broker answers follows the states its own answer is made of, the page's cadence, and each load
 *  that replaces the page's set. */
function waitFor(bindings: (Binding | undefined)[], context: ResolveContext): string {
  return bindings.map((binding) => (binding ? waitOf(binding, context) : '')).join('|');
}

export interface BindingState {
  loading: boolean;
  value: BindingResult;
  error: boolean;
}

interface Resolved {
  key: string;
  value: BindingResult;
  error: boolean;
}

/** Resolve a single binding, re-running when the binding or context changes.
 *  State is only written from the async callback; `loading` is derived from
 *  whether the latest resolution matches the current request key. */
export function useBinding(binding: Binding | undefined, context: ResolveContext): BindingState {
  const key = binding ? JSON.stringify(binding) : '';
  const waiting = waitFor([binding], context);
  const latestContext = useLatest(context);
  const [resolved, setResolved] = useState<Resolved>({ key: '\u0000init', value: null, error: false });

  useEffect(() => {
    if (!binding) return;
    let alive = true;
    resolveBinding(binding, latestContext.current).then(
      (value) => alive && setResolved({ key, value, error: false }),
      () => alive && setResolved({ key, value: null, error: true }),
    );
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, waiting, context.scopeId, context.compareArchetype]);

  if (!binding) return { loading: false, value: null, error: false };
  const matched = resolved.key === key;
  return { loading: !matched, value: matched ? resolved.value : null, error: matched && resolved.error };
}

export function useBindings(bindings: (Binding | undefined)[], context: ResolveContext): BindingState[] {
  const key = JSON.stringify(bindings);
  const waiting = waitFor(bindings, context);
  const latestContext = useLatest(context);
  const [resolved, setResolved] = useState<{ key: string; states: BindingState[] }>({
    key: '\u0000init',
    states: [],
  });

  useEffect(() => {
    let alive = true;
    Promise.all(
      bindings.map((b) =>
        b
          ? resolveBinding(b, latestContext.current).then(
              (value): BindingState => ({ loading: false, value, error: false }),
              (): BindingState => ({ loading: false, value: null, error: true }),
            )
          : Promise.resolve<BindingState>({ loading: false, value: null, error: false }),
      ),
    ).then((states) => {
      if (alive) setResolved({ key, states });
    });
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, waiting, context.scopeId, context.compareArchetype]);

  if (resolved.key === key && resolved.states.length === bindings.length) return resolved.states;
  return bindings.map((b) => ({ loading: !!b, value: null, error: false }));
}
