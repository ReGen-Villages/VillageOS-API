/**
 * Hooks that drive the configuration-driven dashboard: build a resolve context from the live model store
 * and the selected scope.
 */
import { useMemo } from 'react';
import {
  discoverDashboardsFromIndex,
  modelIndexFor,
  type DashboardDescriptor,
  type ModelIndex,
  type ModelReads,
  type ResolveContext,
} from '@tendril-ui/tendril';
import { useModelStore } from '../stores/modelStore';
import { usePlatformPagesStore } from '../stores/platformPagesStore';
import { useUiStore } from '../stores/uiStore';

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
 *  and `useBinding` decides it per binding. A factory rather than the reads themselves is
 *  also what keeps this file off the broker, so the page a submitter opens with no credential can
 *  use these hooks. */
export function useResolveContext(
  index: ModelIndex,
  scopeId: string | null,
  compareArchetype: string | undefined,
  makeReads: () => ModelReads,
  nonce = 0,
  serverRefresh = 0,
  wrote?: () => void,
): ResolveContext {
  const stateVersions = useUiStore((s) => s.stateVersions);
  const loadsApplied = useModelStore((s) => s.loadsApplied);
  return useMemo(
    () => ({ index, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, loadsApplied, reads: makeReads(), wrote }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [index, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, loadsApplied, wrote],
  );
}
