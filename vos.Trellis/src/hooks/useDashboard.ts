/**
 * Hooks that drive the config-driven dashboard: build a resolve context from the live model store
 * and the selected scope, and resolve each binding again when what its own answer is made of moves.
 */
import { useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import {
  discoverDashboardsFromIndex,
  modelIndexFor,
  resolveBinding,
  type BindingResult,
  type ModelIndex,
  type ResolveContext,
} from '../api/dashboardApi';
import { askedOfTheBroker, statesRead } from '../api/bindingRefresh';
import type { ModelReads } from '../api/modelReads';
import { useModelStore } from '../stores/modelStore';
import { usePlatformPagesStore } from '../stores/platformPagesStore';
import { useUiStore } from '../stores/uiStore';
import type { Binding, DashboardDescriptor } from '../types/dashboard';

/** The index over the loaded model. Shared rather than memoized per component, so the navigation
 *  and the page it points at read one index instead of each walking the whole model. */
export function useModelIndex(): ModelIndex {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  return modelIndexFor(things, relationships);
}

/** The pages the platform declares for the signed-in account, then the dashboards the loaded model
 *  publishes, ordered by name. The platform's come first because they are the same for every model
 *  a session opens, and a page that moved when the model changed would be a page a reader loses. */
export function useDashboards(): DashboardDescriptor[] {
  const declared = usePlatformPagesStore((s) => s.pages);
  const published = discoverDashboardsFromIndex(useModelIndex());
  return useMemo(() => [...declared, ...published], [declared, published]);
}

/** Wrap a shared model index + the selected scope into a resolve context.
 *
 *  `makeReads` is called once per generation rather than passed as a value, because what it
 *  builds shares the reads of that generation: widgets asking the same question ask once between
 *  them rather than once each. It is discarded whenever anything that could change an answer moves
 *  — a live event, a state, the cadence — so a generation never hands a reader the answer to a
 *  question the model has since moved past. Which widgets then resolve again is a separate matter,
 *  and {@link useBinding} decides it per binding. A factory rather than the reads themselves is
 *  also what keeps this file off the broker, so the page a submitter opens with no credential can
 *  use these hooks. */
export function useResolveContext(
  idx: ModelIndex,
  scopeId: string | null,
  compareArchetype: string | undefined,
  makeReads: () => ModelReads,
  nonce = 0,
  serverRefresh = 0,
  wrote?: () => void,
): ResolveContext {
  const stateVersions = useUiStore((s) => s.stateVersions);
  return useMemo(
    () => ({ idx, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, reads: makeReads(), wrote }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [idx, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, wrote],
  );
}

/** A number naming one index, so what a binding waits for is one comparable value whether its
 *  answer comes from the loaded model or from the broker. */
const generations = new WeakMap<ModelIndex, number>();
let generationsGiven = 0;

function generationOf(idx: ModelIndex): number {
  const known = generations.get(idx);
  if (known !== undefined) return known;
  generationsGiven += 1;
  generations.set(idx, generationsGiven);
  return generationsGiven;
}

/** The newest context, for an effect that must resolve against it without re-running whenever it
 *  is replaced. Written in an effect of its own, declared first, so it is current before any effect
 *  below it reads it. */
function useLatest(ctx: ResolveContext): RefObject<ResolveContext> {
  const held = useRef(ctx);
  useEffect(() => {
    held.current = ctx;
  });
  return held;
}

function waitOf(binding: Binding, ctx: ResolveContext): string {
  if (!askedOfTheBroker(binding)) return `model:${generationOf(ctx.idx)}`;
  const versions = ctx.stateVersions ?? {};
  const states = statesRead(binding).map((state) => `${state}=${versions[state] ?? 0}`);
  return `broker:${ctx.serverRefresh ?? 0}:${states.join(',')}`;
}

/** What these bindings are waiting for, as one value that moves when — and only when — one of them
 *  has to be resolved again. A figure read from the loaded model follows that model; a figure the
 *  broker answers follows the states its own answer is made of, and the page's cadence. */
function waitFor(bindings: (Binding | undefined)[], ctx: ResolveContext): string {
  return bindings.map((binding) => (binding ? waitOf(binding, ctx) : '')).join('|');
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
  const waiting = waitFor([binding], ctx);
  const context = useLatest(ctx);
  const [resolved, setResolved] = useState<Resolved>({ key: '\u0000init', value: null, error: false });

  useEffect(() => {
    if (!binding) return;
    let alive = true;
    resolveBinding(binding, context.current).then(
      (value) => alive && setResolved({ key, value, error: false }),
      () => alive && setResolved({ key, value: null, error: true }),
    );
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, waiting, ctx.scopeId, ctx.compareArchetype]);

  if (!binding) return { loading: false, value: null, error: false };
  const matched = resolved.key === key;
  return { loading: !matched, value: matched ? resolved.value : null, error: matched && resolved.error };
}

/** Resolve a list of bindings in parallel (e.g. funnel stage counts). */
export function useBindings(bindings: (Binding | undefined)[], ctx: ResolveContext): BindingState[] {
  const key = JSON.stringify(bindings);
  const waiting = waitFor(bindings, ctx);
  const context = useLatest(ctx);
  const [resolved, setResolved] = useState<{ key: string; states: BindingState[] }>({
    key: '\u0000init',
    states: [],
  });

  useEffect(() => {
    let alive = true;
    Promise.all(
      bindings.map((b) =>
        b
          ? resolveBinding(b, context.current).then(
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
  }, [key, waiting, ctx.scopeId, ctx.compareArchetype]);

  if (resolved.key === key && resolved.states.length === bindings.length) return resolved.states;
  return bindings.map((b) => ({ loading: !!b, value: null, error: false }));
}
