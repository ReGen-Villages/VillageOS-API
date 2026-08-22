/**
 * Hooks that drive the config-driven dashboard: build a resolve context from
 * the live model store + selected scope, and resolve bindings (re-running when
 * scope or model data changes).
 */
import { useEffect, useMemo, useState } from 'react';
import {
  discoverDashboardsFromIndex,
  modelIndexFor,
  resolveBinding,
  type BindingResult,
  type ModelIndex,
  type ResolveContext,
} from '../api/dashboardApi';
import { useModelStore } from '../stores/modelStore';
import type { Binding, DashboardDescriptor } from '../types/dashboard';

/** The index over the loaded model. Shared rather than memoized per component, so the navigation
 *  and the page it points at read one index instead of each walking the whole model. */
export function useModelIndex(): ModelIndex {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  return modelIndexFor(things, relationships);
}

/** The dashboards the loaded model publishes, ordered by name. */
export function useDashboards(): DashboardDescriptor[] {
  return discoverDashboardsFromIndex(useModelIndex());
}

/** Wrap a shared model index + the selected scope into a resolve context. `nonce` is
 *  part of the context identity only: bumping it forces server-side bindings to
 *  re-resolve on live events without rebuilding the (unchanged) index.
 *
 *  The context carries the state reads of one refresh generation, so widgets asking about the
 *  same state ask the broker once between them rather than once each. It is discarded whenever
 *  the identity changes — which is every live event and every timed refresh, the same moments
 *  that make a widget re-resolve at all. */
export function useResolveContext(
  idx: ModelIndex,
  scopeId: string | null,
  compareArchetype: string | undefined,
  nonce = 0,
): ResolveContext {
  return useMemo(
    () => ({ idx, scopeId, compareArchetype, nonce, stateMembers: new Map(), thingRanges: new Map() }),
    [idx, scopeId, compareArchetype, nonce],
  );
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
export function useBinding(binding: Binding | undefined, ctx: ResolveContext): BindingState {
  const key = binding ? JSON.stringify(binding) : '';
  const [resolved, setResolved] = useState<Resolved>({ key: '\u0000init', value: null, error: false });

  useEffect(() => {
    if (!binding) return;
    let alive = true;
    resolveBinding(binding, ctx).then(
      (value) => alive && setResolved({ key, value, error: false }),
      () => alive && setResolved({ key, value: null, error: true }),
    );
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, ctx]);

  if (!binding) return { loading: false, value: null, error: false };
  const matched = resolved.key === key;
  return { loading: !matched, value: matched ? resolved.value : null, error: matched && resolved.error };
}

/** Resolve a list of bindings in parallel (e.g. funnel stage counts). */
export function useBindings(bindings: (Binding | undefined)[], ctx: ResolveContext): BindingState[] {
  const key = JSON.stringify(bindings);
  const [resolved, setResolved] = useState<{ key: string; states: BindingState[] }>({
    key: '\u0000init',
    states: [],
  });

  useEffect(() => {
    let alive = true;
    Promise.all(
      bindings.map((b) =>
        b
          ? resolveBinding(b, ctx).then(
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
  }, [key, ctx]);

  if (resolved.key === key && resolved.states.length === bindings.length) return resolved.states;
  return bindings.map((b) => ({ loading: !!b, value: null, error: false }));
}
